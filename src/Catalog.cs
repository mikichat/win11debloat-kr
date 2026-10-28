using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace Win11DebloatKR
{
    /// <summary>하나의 디버트 기능(FeatureId)에 대한 정보.</summary>
    internal sealed class FeatureInfo
    {
        public string Id;
        public string Label;        // 한국어
        public string ToolTip;      // 한국어
        public string ApplyText;    // 한국어
        public string UndoLabel;    // 한국어
        public string ApplyUndoText;// 한국어
        public string CategoryName; // Features.json 의 영문 분류 이름
        public string RegistryKey;
        public string RegistryUndoKey;
        public int? MinVersion;
        public int? MaxVersion;
    }

    /// <summary>그룹(라디오 선택지) 안의 개별 옵션 하나.</summary>
    internal sealed class GroupValueInfo
    {
        public string FeatureId;
        public string Label;      // 한국어
        public bool IsDefault;    // 원본 라벨에 "(Default)" 가 있으면 기본 옵션
    }

    /// <summary>서로 배타적으로 선택해야 하는 기능 묶음.</summary>
    internal sealed class GroupInfo
    {
        public string Id;
        public string Label;       // 한국어
        public string ToolTip;     // 한국어
        public string CategoryName;
        public readonly List<GroupValueInfo> Values = new List<GroupValueInfo>();

        public GroupValueInfo DefaultValue
        {
            get
            {
                foreach (GroupValueInfo value in Values)
                {
                    if (value.IsDefault) return value;
                }
                return Values.Count > 0 ? Values[0] : null;
            }
        }
    }

    /// <summary>기능 분류(탭/그룹 박스).</summary>
    internal sealed class CategoryInfo
    {
        public string Id;
        public string Name;   // Features.json 의 영문 이름
        public string Label;  // 한국어
    }

    /// <summary>제거 대상 앱 하나. 하나의 앱이 여러 패키지 ID 를 가질 수 있다.</summary>
    internal sealed class AppInfo
    {
        public string Id;              // 대표 ID (언어 카탈로그 조회 키)
        public List<string> AppIds;    // 제거 시 전달할 전체 패키지 ID
        public string FriendlyName;  // 한국어
        public string Description;   // 한국어
        public bool SelectedByDefault;
        public string Recommendation;
        public string RemovalMethod;
    }

    /// <summary>Apps.json 의 앱 묶음 프리셋(게이밍 앱, OEM 앱 등).</summary>
    internal sealed class AppPresetInfo
    {
        public string Name;
        public readonly List<string> AppIds = new List<string>();
    }

    /// <summary>
    /// win11debloat 저장소의 Config 폴더(영문 원본 + ko-KR 언어팩)를 읽어
    /// GUI 에 필요한 모든 데이터를 한국어로 제공하는 모델.
    /// </summary>
    internal sealed class Catalog
    {
        public const string LanguageCode = "ko-KR";

        public readonly List<CategoryInfo> Categories = new List<CategoryInfo>();
        public readonly List<FeatureInfo> Features = new List<FeatureInfo>();
        public readonly List<GroupInfo> Groups = new List<GroupInfo>();
        public readonly List<AppInfo> Apps = new List<AppInfo>();
        public readonly List<AppPresetInfo> Presets = new List<AppPresetInfo>();
        public readonly List<string> DefaultFeatureIds = new List<string>();
        public readonly List<string> LastUsedFeatureIds = new List<string>();

        private readonly Dictionary<string, FeatureInfo> _featureById = new Dictionary<string, FeatureInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GroupInfo> _groupById = new Dictionary<string, GroupInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CategoryInfo> _categoryById = new Dictionary<string, CategoryInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CategoryInfo> _categoryByName = new Dictionary<string, CategoryInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>그룹(라디오) 안에서만 선택할 수 있는 FeatureId 집합.</summary>
        public readonly HashSet<string> GroupMemberFeatureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FeatureInfo Feature(string id)
        {
            FeatureInfo info;
            return _featureById.TryGetValue(id ?? string.Empty, out info) ? info : null;
        }

        public GroupInfo Group(string id)
        {
            GroupInfo info;
            return _groupById.TryGetValue(id ?? string.Empty, out info) ? info : null;
        }

        public CategoryInfo Category(string id)
        {
            CategoryInfo info;
            return _categoryById.TryGetValue(id ?? string.Empty, out info) ? info : null;
        }

        /// <summary>기능/그룹이 속한 분류를 영문 이름으로 찾는다.</summary>
        public CategoryInfo CategoryByName(string englishName)
        {
            if (string.IsNullOrWhiteSpace(englishName)) return null;
            CategoryInfo info;
            return _categoryByName.TryGetValue(englishName, out info) ? info : null;
        }

        public static Catalog Load(string payloadRoot)
        {
            Catalog catalog = new Catalog();
            catalog.LoadInternal(payloadRoot);
            return catalog;
        }

        private void LoadInternal(string payloadRoot)
        {
            string configDir = Path.Combine(payloadRoot, "Config");
            string langDir = Path.Combine(configDir, Path.Combine("Languages", LanguageCode));

            Dictionary<string, object> baseFeatures = ReadJson(Path.Combine(configDir, "Features.json"));
            Dictionary<string, object> baseApps = ReadJson(Path.Combine(configDir, "Apps.json"));
            Dictionary<string, object> koFeatures = ReadJson(Path.Combine(langDir, "Features.json"));
            Dictionary<string, object> koCategories = ReadJson(Path.Combine(langDir, "Categories.json"));
            Dictionary<string, object> koApps = ReadJson(Path.Combine(langDir, "Apps.json"));

            // ---- 분류 ----
            foreach (object raw in AsList(AsDict(baseFeatures)["Categories"]))
            {
                Dictionary<string, object> item = AsDict(raw);
                if (item == null) continue;

                CategoryInfo category = new CategoryInfo
                {
                    Id = Str(item, "CategoryId"),
                    Name = Str(item, "Name")
                };
                if (string.IsNullOrEmpty(category.Id)) continue;

                category.Label = TranslateCategory(koCategories, category.Id, category.Name);
                Categories.Add(category);
                _categoryById[category.Id] = category;
                if (!string.IsNullOrEmpty(category.Name)) _categoryByName[category.Name] = category;
            }

            // ---- 기능 ----
            Dictionary<string, object> koFeatureEntries = AsDict(AsDict(koFeatures)["Features"]) ?? new Dictionary<string, object>();
            foreach (object raw in AsList(AsDict(baseFeatures)["Features"]))
            {
                Dictionary<string, object> item = AsDict(raw);
                if (item == null) continue;

                string id = Str(item, "FeatureId");
                if (string.IsNullOrEmpty(id)) continue;

                object koEntry;
                koFeatureEntries.TryGetValue(id, out koEntry);
                Dictionary<string, object> ko = AsDict(koEntry);
                FeatureInfo feature = new FeatureInfo
                {
                    Id = id,
                    Label = TranslateField(ko, "Label", Str(item, "Label")),
                    ToolTip = TranslateField(ko, "ToolTip", Str(item, "ToolTip")),
                    ApplyText = TranslateField(ko, "ApplyText", Str(item, "ApplyText")),
                    UndoLabel = TranslateField(ko, "UndoLabel", Str(item, "UndoLabel")),
                    ApplyUndoText = TranslateField(ko, "ApplyUndoText", Str(item, "ApplyUndoText")),
                    CategoryName = Str(item, "Category"),
                    RegistryKey = Str(item, "RegistryKey"),
                    RegistryUndoKey = Str(item, "RegistryUndoKey"),
                    MinVersion = ReadInt(item, "MinVersion"),
                    MaxVersion = ReadInt(item, "MaxVersion")
                };
                if (string.IsNullOrEmpty(feature.ToolTip)) feature.ToolTip = null;

                Features.Add(feature);
                _featureById[id] = feature;
            }

            // ---- 그룹(라디오 묶음) ----
            Dictionary<string, object> koGroupEntries = AsDict(AsDict(koFeatures)["UiGroups"]) ?? new Dictionary<string, object>();
            foreach (object raw in AsList(AsDict(baseFeatures)["UiGroups"]))
            {
                Dictionary<string, object> item = AsDict(raw);
                if (item == null) continue;

                string id = Str(item, "GroupId");
                if (string.IsNullOrEmpty(id)) continue;

                object koGroupEntry;
                koGroupEntries.TryGetValue(id, out koGroupEntry);
                Dictionary<string, object> koGroup = AsDict(koGroupEntry);
                Dictionary<string, object> koValues = koGroup == null ? null : AsDict(koGroup["Values"]);

                GroupInfo group = new GroupInfo
                {
                    Id = id,
                    Label = TranslateField(koGroup, "Label", Str(item, "Label")),
                    ToolTip = TranslateField(koGroup, "ToolTip", Str(item, "ToolTip")),
                    CategoryName = Str(item, "Category")
                };
                if (string.IsNullOrEmpty(group.ToolTip)) group.ToolTip = null;

                foreach (object rawValue in AsList(item["Values"]))
                {
                    Dictionary<string, object> valueItem = AsDict(rawValue);
                    if (valueItem == null) continue;

                    List<string> featureIds = new List<string>();
                    foreach (object fid in AsList(valueItem["FeatureIds"]))
                    {
                        string text = Convert.ToString(fid, CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(text)) featureIds.Add(text);
                    }
                    if (featureIds.Count == 0) continue;

                    string englishLabel = Str(valueItem, "Label");
                    foreach (string fid in featureIds)
                    {
                        string koreanLabel = englishLabel;
                        object koValueEntry;
                        if (koValues != null && koValues.TryGetValue(fid, out koValueEntry))
                        {
                            string translated = Str(AsDict(koValueEntry), "Label");
                            if (!string.IsNullOrEmpty(translated)) koreanLabel = translated;
                        }

                        GroupValueInfo value = new GroupValueInfo
                        {
                            FeatureId = fid,
                            Label = koreanLabel,
                            IsDefault = englishLabel.IndexOf("(Default)", StringComparison.OrdinalIgnoreCase) >= 0
                        };
                        group.Values.Add(value);
                        GroupMemberFeatureIds.Add(fid);
                    }
                }

                if (group.Values.Count == 0) continue;
                Groups.Add(group);
                _groupById[id] = group;
            }

            // ---- 앱 ----
            Dictionary<string, object> koAppEntries = koApps ?? new Dictionary<string, object>();
            foreach (object raw in AsList(AsDict(baseApps)["Apps"]))
            {
                Dictionary<string, object> item = AsDict(raw);
                if (item == null) continue;

                // AppId 은 단일 문자열이거나 여러 패키지 ID 로 이루어진 배열일 수 있다.
                List<string> appIds = ReadAppIds(item);
                if (appIds.Count == 0) continue;

                string primaryId = appIds[0];

                // 언어 카탈로그는 대표 ID 로 조회한다.
                object koEntry;
                koAppEntries.TryGetValue(primaryId, out koEntry);
                Dictionary<string, object> ko = AsDict(koEntry);

                AppInfo app = new AppInfo
                {
                    Id = primaryId,
                    AppIds = appIds,
                    FriendlyName = TranslateField(ko, "FriendlyName", primaryId),
                    Description = TranslateField(ko, "Description", string.Empty),
                    SelectedByDefault = ReadBool(item, "SelectedByDefault"),
                    Recommendation = Str(item, "Recommendation"),
                    RemovalMethod = Str(item, "RemovalMethod")
                };
                if (string.IsNullOrEmpty(app.Description)) app.Description = string.Empty;

                Apps.Add(app);
            }

            // ---- 앱 프리셋(게이밍 앱 / OEM 앱) ----
            foreach (object rawPreset in AsList(AsDict(baseApps)["Presets"]))
            {
                Dictionary<string, object> presetItem = AsDict(rawPreset);
                if (presetItem == null) continue;

                AppPresetInfo preset = new AppPresetInfo { Name = Str(presetItem, "Name") };
                foreach (object rawId in AsList(presetItem["AppIds"]))
                {
                    string id = Convert.ToString(rawId, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(id)) preset.AppIds.Add(id);
                }
                if (preset.AppIds.Count > 0) Presets.Add(preset);
            }

            // ---- 기본 프리셋 ----
            foreach (string id in ReadSettingsIds(Path.Combine(configDir, "DefaultSettings.json")))
            {
                DefaultFeatureIds.Add(id);
            }

            // ---- 마지막 사용 설정 (있을 때만) ----
            string lastUsed = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.Combine("Win11Debloat", "LastUsedSettings.json"));
            foreach (string id in ReadSettingsIds(lastUsed))
            {
                LastUsedFeatureIds.Add(id);
            }
        }

        private static List<string> ReadSettingsIds(string path)
        {
            List<string> result = new List<string>();
            if (!File.Exists(path)) return result;

            Dictionary<string, object> json;
            try
            {
                json = ReadJson(path);
            }
            catch
            {
                return result;
            }
            if (json == null) return result;

            foreach (object raw in AsList(json["Settings"]))
            {
                Dictionary<string, object> item = AsDict(raw);
                if (item == null) continue;
                if (!ReadBool(item, "Value")) continue;
                string name = Str(item, "Name");
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            return result;
        }

        /// <summary>앱 항목의 AppId 값(단일 문자열 또는 배열)을 목록으로 읽는다.</summary>
        private static List<string> ReadAppIds(Dictionary<string, object> item)
        {
            List<string> result = new List<string>();
            object raw;
            if (!item.TryGetValue("AppId", out raw) || raw == null) return result;

            if (raw is string)
            {
                string single = ((string)raw).Trim();
                if (single.Length > 0) result.Add(single);
                return result;
            }

            foreach (object one in AsList(raw))
            {
                string text = Convert.ToString(one, CultureInfo.InvariantCulture);
                if (text == null) continue;
                text = text.Trim();
                if (text.Length > 0) result.Add(text);
            }
            return result;
        }

        private static string TranslateCategory(Dictionary<string, object> koCategories, string categoryId, string fallback)
        {
            if (koCategories == null) return fallback;
            object entry;
            if (!koCategories.TryGetValue(categoryId, out entry)) return fallback;
            Dictionary<string, object> entryMap = AsDict(entry);
            if (entryMap == null) return fallback;
            string label = Str(entryMap, "Label");
            return string.IsNullOrEmpty(label) ? fallback : label;
        }

        private static string TranslateField(Dictionary<string, object> entry, string field, string fallback)
        {
            if (entry == null) return fallback;
            string value = Str(entry, field);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        // ---- JSON 헬퍼 ----

        internal static Dictionary<string, object> ReadJson(string path)
        {
            string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            object value = serializer.DeserializeObject(text);
            return AsDict(value);
        }

        internal static Dictionary<string, object> AsDict(object value)
        {
            return value as Dictionary<string, object>;
        }

        internal static List<object> AsList(object value)
        {
            if (value == null) return new List<object>();
            object[] array = value as object[];
            if (array != null) return new List<object>(array);
            System.Collections.ArrayList list = value as System.Collections.ArrayList;
            if (list != null) return new List<object>(list.ToArray());
            return new List<object>();
        }

        private static string Str(Dictionary<string, object> item, string key)
        {
            if (item == null) return string.Empty;
            object value;
            if (!item.TryGetValue(key, out value) || value == null) return string.Empty;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(Dictionary<string, object> item, string key)
        {
            if (item == null) return false;
            object value;
            if (!item.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed;
        }

        private static int? ReadInt(Dictionary<string, object> item, string key)
        {
            if (item == null) return null;
            object value;
            if (!item.TryGetValue(key, out value) || value == null) return null;
            int parsed;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return null;
        }
    }
}
