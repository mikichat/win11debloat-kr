using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Win11DebloatKR
{
    /// <summary>
    /// Win11Debloat 한국어 GUI 의 주 창.
    /// 모든 메뉴/버튼/설명은 한국어로 제공되며, 실제 작업은
    /// win11debloat 의 PowerShell 엔진에 위임한다.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly Catalog _catalog;
        private readonly string _payloadRoot;
        private readonly Runner _runner = new Runner();

        // 기본 탭
        private readonly CheckBox _presetDefault = new CheckBox();
        private readonly CheckBox _presetPrivacy = new CheckBox();
        private readonly CheckBox _presetAi = new CheckBox();
        private readonly CheckBox _presetLastUsed = new CheckBox();
        private readonly CheckBox _chkRestorePoint = new CheckBox();
        private readonly CheckBox _chkRegistryBackup = new CheckBox();
        private readonly CheckBox _chkSkipExplorer = new CheckBox();
        private readonly CheckBox _chkSysprep = new CheckBox();
        private readonly CheckBox _chkStartLayout = new CheckBox();
        private readonly CheckBox _chkStartLayoutAllUsers = new CheckBox();
        private readonly TextBox _txtStartLayout = new TextBox();
        private readonly CheckBox _chkGamingApps = new CheckBox();
        private readonly CheckBox _chkHpApps = new CheckBox();
        private readonly CheckBox _chkForceEdge = new CheckBox();
        private readonly TextBox _txtUser = new TextBox();
        private readonly TextBox _txtPreview = new TextBox();

        // 기능 탭
        private readonly ListBox _categoryList = new ListBox();
        private readonly Panel _featureHost = new Panel();
        private readonly FlowLayoutPanel _featurePanel = new FlowLayoutPanel();
        private readonly TextBox _featureSearch = new TextBox();
        private readonly Label _featureCount = new Label();
        private readonly Dictionary<string, CheckBox> _featureBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RadioButton> _groupRadios = new Dictionary<string, RadioButton>(StringComparer.OrdinalIgnoreCase);
        /// <summary>그룹별 현재 선택된 FeatureId. 빈 문자열은 '변경 없음' 을 뜻한다.</summary>
        private readonly Dictionary<string, string> _groupSelection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const string NoChange = "";
        private int _visibleFeatureCount;
        private int _lastHostWidth;

        // 앱 제거 탭
        private readonly ListView _appList = new ListView();
        private readonly TextBox _appSearch = new TextBox();
        private readonly Label _appCount = new Label();

        // 공통
        private readonly RichTextBox _logBox = new RichTextBox();
        private readonly Label _summaryLabel = new Label();
        private readonly Label _previewLabel = new Label();
        private readonly Button _applyButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly TabControl _tabs = new TabControl();
        private readonly StatusStrip _status = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly MenuStrip _menu = new MenuStrip();
        private readonly Panel _bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(8) };
        private bool _updatingPresets;
        private bool _updatingAppList;
        private readonly bool _previewMode;
        private readonly bool _selfTest;

        public MainForm(string payloadRoot, bool previewMode, int initialTab, bool selfTest)
        {
            _payloadRoot = payloadRoot;
            _previewMode = previewMode;
            _selfTest = selfTest;
            _catalog = Catalog.Load(payloadRoot);

            Text = "Win11Debloat 한국어 도구";
            Icon = AppIcon.Create();
            Font = CreateKoreanFont(9F);
            ClientSize = new Size(1120, 780);
            MinimumSize = new Size(940, 660);
            StartPosition = FormStartPosition.CenterScreen;

            BuildMenu();
            BuildTabs();
            BuildBottomBar();
            BuildStatusBar();

            // 채움 → 아래쪽 → 위쪽 → 상태표시줄 순으로 추가해야 도킹 순서가 맞다.
            Controls.Add(_tabs);
            Controls.Add(_bottomBar);
            Controls.Add(_menu);
            Controls.Add(_status);

            _runner.Output += OnRunnerOutput;

            BuildCategoryList();
            ApplyDefaultSelections();
            RebuildFeaturePanel();
            RefreshAppList();
            UpdateSummary();

            if (_previewMode)
            {
                _applyButton.Enabled = false;
                _applyButton.Text = "적용하기 (미리보기 모드)";
                _summaryLabel.Text = "미리보기 모드 — 관리자 권한 없이 UI 만 확인합니다. 실제 적용은 '--preview' 없이 실행하세요.";
                _statusLabel.Text = "미리보기 모드";
                WriteDiagnostics();
            }

            if (initialTab >= 0 && initialTab < _tabs.TabPages.Count)
            {
                _tabs.SelectedIndex = initialTab;
            }

            if (_selfTest)
            {
                StartSelfTest();
            }
        }

        /// <summary>
        /// 시스템에 아무 변화도 만들지 않으면서 내장 스크립트와 PowerShell 실행 경로를
        /// 확인하는 진단 검사.
        /// -WhatIf 를 사용하면 레지스트리 쓰기가 실제로 수행되지 않으므로
        /// 실제 적용 파이프라인(기능 선택 → 진행 표시 → 완료)까지 그대로 확인할 수 있다.
        /// </summary>
        private void StartSelfTest()
        {
            try
            {
                string scriptPath = Path.Combine(_payloadRoot, "Win11Debloat.ps1");
                if (!File.Exists(scriptPath))
                {
                    AppendLog("내장 스크립트를 찾을 수 없습니다: " + scriptPath, true);
                    return;
                }

                List<string> args = new List<string>();
                args.Add("-Language");
                args.Add("'ko-KR'");
                args.Add("-Silent");
                args.Add("-SkipExplorerRestart");
                args.Add("-WhatIf");
                // 레지스트리 기반 기능만 사용한다. (앱 제거/예약 작업/저장소 DB 등
                // 부수 효과가 있는 기능은 -WhatIf 로도 실제 변경이 발생하므로 제외)
                args.Add("-DisableSuggestions");
                args.Add("-DisableLockscreenTips");
                args.Add("-DisableEdgeAds");
                args.Add("-ShowKnownFileExt");
                args.Add("-HideChat");

                _tabs.SelectedIndex = 3;
                _logBox.Clear();
                AppendLog("=== 자체 진단 검사 (WhatIf · 시스템 변경 없음) ===", false);
                AppendLog("스크립트: " + scriptPath, false);
                AppendLog("", false);

                _applyButton.Enabled = false;
                _cancelButton.Enabled = true;
                _statusLabel.Text = "자체 진단 검사 중...";
                _runner.Start(scriptPath, args, _payloadRoot);
            }
            catch (Exception ex)
            {
                AppendLog("자체 진단 검사 실패: " + ex.GetType().Name + " : " + ex.Message, true);
                AppendLog(ex.ToString(), true);
                _statusLabel.Text = "자체 진단 검사 실패";
            }
        }

        /// <summary>검증용 진단 리포트를 남긴다.</summary>
        private void WriteDiagnostics()
        {
            try
            {
                int checkedFeatures = 0;
                foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
                {
                    if (pair.Value.Checked) checkedFeatures++;
                }
                int checkedGroups = 0;
                foreach (KeyValuePair<string, RadioButton> pair in _groupRadios)
                {
                    if (pair.Value.Checked) checkedGroups++;
                }

                StringBuilder report = new StringBuilder();
                report.AppendLine("분류 수            : " + _catalog.Categories.Count);
                report.AppendLine("기능 수(전체)      : " + _catalog.Features.Count);
                report.AppendLine("그룹 수            : " + _catalog.Groups.Count);
                report.AppendLine("그룹 옵션 수       : " + _catalog.GroupMemberFeatureIds.Count);
                report.AppendLine("앱 수              : " + _catalog.Apps.Count);
                report.AppendLine("앱 프리셋 수       : " + _catalog.Presets.Count);
                report.AppendLine("기본 프리셋 항목 수: " + _catalog.DefaultFeatureIds.Count);
                report.AppendLine("마지막 사용 항목 수: " + _catalog.LastUsedFeatureIds.Count);
                report.AppendLine("생성된 체크박스 수 : " + _featureBoxes.Count);
                report.AppendLine("체크된 체크박스 수 : " + checkedFeatures);
                report.AppendLine("라디오 항목 수     : " + _groupRadios.Count);
                report.AppendLine("체크된 라디오 수   : " + checkedGroups);
                report.AppendLine("표시 중인 항목 수  : " + _visibleFeatureCount);
                report.AppendLine();
                report.AppendLine("생성된 인자:");
                report.AppendLine("  " + string.Join(" ", BuildArguments().ToArray()));

                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Win11DebloatKR", "diagnose.txt"));
                File.WriteAllText(path, report.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 진단 실패는 무시한다.
            }
        }

        // ------------------------------------------------------------------
        // 메뉴
        // ------------------------------------------------------------------
        private void BuildMenu()
        {
            ToolStripMenuItem fileMenu = new ToolStripMenuItem("파일(&F)");
            fileMenu.DropDownItems.Add("로그 폴더 열기", null, (s, e) => OpenFolder(LogsFolder));
            fileMenu.DropDownItems.Add("내장 스크립트 폴더 열기", null, (s, e) => OpenFolder(_payloadRoot));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("종료(&X)", null, (s, e) => Close());

            ToolStripMenuItem optionMenu = new ToolStripMenuItem("옵션(&O)");
            optionMenu.DropDownItems.Add("관리자 권한으로 다시 실행", null, (s, e) => RelaunchElevated());
            optionMenu.DropDownItems.Add("선택 항목 초기화", null, (s, e) => ResetSelections());
            optionMenu.DropDownItems.Add(new ToolStripSeparator());
            optionMenu.DropDownItems.Add("언어 설정 정보", null, (s, e) => ShowLanguageInfo());

            ToolStripMenuItem helpMenu = new ToolStripMenuItem("도움말(&H)");
            helpMenu.DropDownItems.Add("사용법 안내", null, (s, e) => ShowUsage());
            helpMenu.DropDownItems.Add("Win11Debloat 정보", null, (s, e) => ShowAbout());

            _menu.Items.Add(fileMenu);
            _menu.Items.Add(optionMenu);
            _menu.Items.Add(helpMenu);
            _menu.Dock = DockStyle.Top;
        }

        // ------------------------------------------------------------------
        // 탭
        // ------------------------------------------------------------------
        private void BuildTabs()
        {
            _tabs.Dock = DockStyle.Fill;

            _tabs.TabPages.Add(BuildBasicTab());
            _tabs.TabPages.Add(BuildFeaturesTab());
            _tabs.TabPages.Add(BuildAppsTab());
            _tabs.TabPages.Add(BuildLogTab());
        }

        private TabPage BuildBasicTab()
        {
            TabPage page = new TabPage("기본");

            // --- 적용 미리보기 (채움) ---
            GroupBox previewBox = new GroupBox
            {
                Text = "적용 미리보기",
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            _txtPreview.Dock = DockStyle.Fill;
            _txtPreview.Multiline = true;
            _txtPreview.ReadOnly = true;
            _txtPreview.ScrollBars = ScrollBars.Vertical;
            _txtPreview.BackColor = Color.White;
            _txtPreview.Font = new Font("Consolas", 9F);
            _txtPreview.WordWrap = false;
            previewBox.Controls.Add(_txtPreview);

            // --- 실행 옵션 ---
            GroupBox optionBox = new GroupBox
            {
                Text = "실행 옵션",
                Dock = DockStyle.Top,
                Height = 152,
                Padding = new Padding(8)
            };
            FlowLayoutPanel optionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            _chkRestorePoint.Text = "시스템 복원 지점 만들기 (변경 사항 되돌리기용)";
            _chkRegistryBackup.Text = "변경 전 레지스트리 백업 만들기 (권장)";
            _chkRegistryBackup.Checked = true;
            _chkSkipExplorer.Text = "탐색기(Explorer) 다시 시작 안 함";
            _chkSysprep.Text = "Sysprep 모드 (Windows 설치 시 이미지용)";
            optionPanel.Controls.Add(_chkRestorePoint);
            optionPanel.Controls.Add(_chkRegistryBackup);
            optionPanel.Controls.Add(_chkSkipExplorer);
            optionPanel.Controls.Add(_chkSysprep);

            FlowLayoutPanel userRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            userRow.Controls.Add(new Label { Text = "대상 사용자: ", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _txtUser.Width = 220;
            userRow.Controls.Add(_txtUser);
            optionPanel.Controls.Add(userRow);

            // 시작 메뉴 레이아웃 교체
            _chkStartLayout.Text = "시작 메뉴 레이아웃(.bin) 교체";
            _chkStartLayoutAllUsers.Text = "모든 사용자에 적용";
            _chkStartLayoutAllUsers.Margin = new Padding(12, 0, 0, 0);
            optionPanel.Controls.Add(_chkStartLayout);

            FlowLayoutPanel layoutRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            _txtStartLayout.Width = 330;
            layoutRow.Controls.Add(new Label { Text = "레이아웃 파일: ", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            layoutRow.Controls.Add(_txtStartLayout);
            Button browse = new Button { Text = "찾아보기...", Size = new Size(96, 26) };
            browse.Click += (s, e) => PickStartLayoutFile();
            layoutRow.Controls.Add(browse);
            layoutRow.Controls.Add(_chkStartLayoutAllUsers);
            optionPanel.Controls.Add(layoutRow);

            optionBox.Controls.Add(optionPanel);

            // --- 빠른 프리셋 ---
            GroupBox presetBox = new GroupBox
            {
                Text = "빠른 프리셋 (체크하면 해당 항목이 자동 선택됩니다)",
                Dock = DockStyle.Top,
                Height = 112,
                Padding = new Padding(8)
            };
            FlowLayoutPanel presetPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            _presetDefault.Text = "기본 정리 (권장 프리셋)";
            _presetPrivacy.Text = "개인정보 보호 강화";
            _presetAi.Text = "AI 기능 정리";
            _presetLastUsed.Text = "마지막으로 사용한 설정";
            _presetLastUsed.Enabled = _catalog.LastUsedFeatureIds.Count > 0;
            if (!_presetLastUsed.Enabled)
            {
                _presetLastUsed.Text = "마지막으로 사용한 설정 (기록 없음)";
            }
            presetPanel.Controls.Add(_presetDefault);
            presetPanel.Controls.Add(_presetPrivacy);
            presetPanel.Controls.Add(_presetAi);
            presetPanel.Controls.Add(_presetLastUsed);

            _presetDefault.CheckedChanged += (s, e) => ApplyPreset(_presetDefault, _catalog.DefaultFeatureIds);
            _presetPrivacy.CheckedChanged += (s, e) => ApplyPreset(_presetPrivacy, GetCategoryFeatureIds("PrivacySuggestedContent"));
            _presetAi.CheckedChanged += (s, e) => ApplyPreset(_presetAi, GetCategoryFeatureIds("AI"));
            _presetLastUsed.CheckedChanged += (s, e) => ApplyPreset(_presetLastUsed, _catalog.LastUsedFeatureIds);
            presetBox.Controls.Add(presetPanel);

            page.Controls.Add(previewBox);
            page.Controls.Add(optionBox);
            page.Controls.Add(presetBox);
            return page;
        }

        private TabPage BuildFeaturesTab()
        {
            TabPage page = new TabPage("기능");

            Panel toolbar = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6) };
            Label searchLabel = new Label { Text = "검색: ", AutoSize = true, Location = new Point(6, 12) };
            _featureSearch.Location = new Point(44, 8);
            _featureSearch.Width = 240;
            _featureSearch.TextChanged += (s, e) => RebuildFeaturePanel();
            Button selectAll = new Button { Text = "전체 선택", Location = new Point(296, 6), Size = new Size(90, 26) };
            selectAll.Click += (s, e) => SetAllFeatures(true);
            Button clearAll = new Button { Text = "전체 해제", Location = new Point(392, 6), Size = new Size(90, 26) };
            clearAll.Click += (s, e) => SetAllFeatures(false);
            _featureCount.Location = new Point(492, 12);
            _featureCount.AutoSize = true;
            toolbar.Controls.Add(searchLabel);
            toolbar.Controls.Add(_featureSearch);
            toolbar.Controls.Add(selectAll);
            toolbar.Controls.Add(clearAll);
            toolbar.Controls.Add(_featureCount);

            _categoryList.Dock = DockStyle.Fill;
            _categoryList.Font = CreateKoreanFont(9F);
            _categoryList.SelectedIndexChanged += (s, e) => RebuildFeaturePanel();

            Panel leftPanel = new Panel { Dock = DockStyle.Left, Width = 200, Padding = new Padding(4) };
            leftPanel.Controls.Add(_categoryList);

            _featurePanel.Dock = DockStyle.Top;
            _featurePanel.AutoSize = true;
            _featurePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _featurePanel.FlowDirection = FlowDirection.TopDown;
            _featurePanel.WrapContents = false;
            _featurePanel.Padding = new Padding(2);

            _featureHost.Dock = DockStyle.Fill;
            _featureHost.AutoScroll = true;
            _featureHost.Padding = new Padding(4);
            _featureHost.Controls.Add(_featurePanel);
            _featureHost.Resize += (s, e) =>
            {
                if (Math.Abs(_featureHost.ClientSize.Width - _lastHostWidth) > 24)
                {
                    RebuildFeaturePanel();
                }
            };

            Panel container = new Panel { Dock = DockStyle.Fill };
            container.Controls.Add(_featureHost);
            container.Controls.Add(leftPanel);

            page.Controls.Add(container);
            page.Controls.Add(toolbar);
            return page;
        }
        private TabPage BuildAppsTab()
        {
            TabPage page = new TabPage("앱 제거");

            Panel toolbar = new Panel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6) };
            Label searchLabel = new Label { Text = "검색: ", AutoSize = true, Location = new Point(6, 12) };
            _appSearch.Location = new Point(44, 8);
            _appSearch.Width = 240;
            _appSearch.TextChanged += (s, e) => RefreshAppList();
            Button selectAll = new Button { Text = "전체 선택", Location = new Point(296, 6), Size = new Size(90, 26) };
            selectAll.Click += (s, e) => SetAllApps(true);
            Button clearAll = new Button { Text = "전체 해제", Location = new Point(392, 6), Size = new Size(90, 26) };
            clearAll.Click += (s, e) => SetAllApps(false);
            Button defaultApps = new Button { Text = "기본 앱 목록", Location = new Point(488, 6), Size = new Size(110, 26) };
            defaultApps.Click += (s, e) => SelectApps(_catalog.Apps.Where(a => a.SelectedByDefault).Select(a => a.Id));
            Button gamingApps = new Button { Text = "게이밍 앱", Location = new Point(604, 6), Size = new Size(100, 26) };
            gamingApps.Click += (s, e) => SelectPresetApps(0);
            Button oemApps = new Button { Text = "OEM 앱", Location = new Point(710, 6), Size = new Size(100, 26) };
            oemApps.Click += (s, e) => SelectPresetApps(1);
            _appCount.Location = new Point(820, 12);
            _appCount.AutoSize = true;
            toolbar.Controls.Add(searchLabel);
            toolbar.Controls.Add(_appSearch);
            toolbar.Controls.Add(selectAll);
            toolbar.Controls.Add(clearAll);
            toolbar.Controls.Add(defaultApps);
            toolbar.Controls.Add(gamingApps);
            toolbar.Controls.Add(oemApps);
            toolbar.Controls.Add(_appCount);

            _appList.Dock = DockStyle.Fill;
            _appList.View = View.Details;
            _appList.FullRowSelect = true;
            _appList.CheckBoxes = true;
            _appList.HideSelection = false;
            _appList.ShowItemToolTips = true;
            _appList.GridLines = true;
            _appList.MultiSelect = true;
            _appList.Columns.Add("앱 이름", 230);
            _appList.Columns.Add("설명", 520);
            _appList.Columns.Add("기본", 50);
            _appList.Columns.Add("제거 방식", 90);
            _appList.ItemChecked += (s, e) =>
            {
                // 목록을 다시 그리는 중에는 이벤트가 재진입하므로 잠시 무시한다.
                if (_updatingAppList) return;
                UpdateSummary();
            };

            // 앱 제거 전용 옵션 (분류이 없는 upstream 기능들)
            Panel optionStrip = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 4, 6, 4) };
            _chkGamingApps.Text = "Xbox 게이밍 앱 제거";
            _chkGamingApps.Location = new Point(6, 2);
            _chkGamingApps.AutoSize = true;
            _chkHpApps.Text = "HP OEM 앱 제거";
            _chkHpApps.Location = new Point(150, 2);
            _chkHpApps.AutoSize = true;
            _chkForceEdge.Text = "Edge 강제 제거 (되돌리기 어려움)";
            _chkForceEdge.Location = new Point(280, 2);
            _chkForceEdge.AutoSize = true;
            _chkGamingApps.CheckedChanged += (s, e) => UpdateSummary();
            _chkHpApps.CheckedChanged += (s, e) => UpdateSummary();
            _chkForceEdge.CheckedChanged += (s, e) => UpdateSummary();
            optionStrip.Controls.Add(_chkGamingApps);
            optionStrip.Controls.Add(_chkHpApps);
            optionStrip.Controls.Add(_chkForceEdge);

            page.Controls.Add(_appList);
            page.Controls.Add(optionStrip);
            page.Controls.Add(toolbar);
            return page;
        }

        private TabPage BuildLogTab()
        {
            TabPage page = new TabPage("로그");

            Panel toolbar = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
            Button clear = new Button { Text = "지우기", Location = new Point(6, 6), Size = new Size(90, 26) };
            clear.Click += (s, e) => _logBox.Clear();
            Button copy = new Button { Text = "복사", Location = new Point(102, 6), Size = new Size(90, 26) };
            copy.Click += (s, e) => CopyLog();
            Button openLogs = new Button { Text = "로그 폴더 열기", Location = new Point(198, 6), Size = new Size(120, 26) };
            openLogs.Click += (s, e) => OpenFolder(LogsFolder);

            CheckBox showOriginal = new CheckBox
            {
                Text = "영문 원문 보기",
                Location = new Point(330, 11),
                Size = new Size(130, 22),
                AutoSize = true,
                Checked = ConsoleTranslator.ShowOriginalText
            };
            showOriginal.CheckedChanged += (s, e) =>
            {
                ConsoleTranslator.ShowOriginalText = showOriginal.Checked;
                _statusLabel.Text = showOriginal.Checked
                    ? "로그를 영문 원문으로 표시합니다. (이전에 출력된 내용은 그대로입니다)"
                    : "로그를 한국어로 표시합니다. (이전에 출력된 내용은 그대로입니다)";
            };

            toolbar.Controls.Add(clear);
            toolbar.Controls.Add(copy);
            toolbar.Controls.Add(openLogs);
            toolbar.Controls.Add(showOriginal);

            _logBox.Dock = DockStyle.Fill;
            _logBox.ReadOnly = true;
            _logBox.BackColor = Color.White;
            _logBox.Font = new Font("Consolas", 9F);
            _logBox.WordWrap = false;

            page.Controls.Add(_logBox);
            page.Controls.Add(toolbar);
            return page;
        }

        // ------------------------------------------------------------------
        // 하단 바 / 상태 표시줄
        // ------------------------------------------------------------------
        private void BuildBottomBar()
        {
            _applyButton.Text = "적용하기";
            _applyButton.Size = new Size(150, 38);
            _applyButton.Dock = DockStyle.Right;
            _applyButton.Font = new Font(Font, FontStyle.Bold);
            _applyButton.Click += (s, e) => StartApply();

            _cancelButton.Text = "취소";
            _cancelButton.Size = new Size(100, 38);
            _cancelButton.Dock = DockStyle.Right;
            _cancelButton.Enabled = false;
            _cancelButton.Click += (s, e) => CancelApply();

            _summaryLabel.Dock = DockStyle.Top;
            _summaryLabel.Height = 22;
            _summaryLabel.AutoEllipsis = true;
            _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;

            _previewLabel.Dock = DockStyle.Fill;
            _previewLabel.Height = 22;
            _previewLabel.AutoEllipsis = true;
            _previewLabel.TextAlign = ContentAlignment.MiddleLeft;
            _previewLabel.ForeColor = Color.DimGray;

            _bottomBar.Controls.Add(_previewLabel);
            _bottomBar.Controls.Add(_summaryLabel);
            _bottomBar.Controls.Add(_cancelButton);
            _bottomBar.Controls.Add(_applyButton);
        }

        private void BuildStatusBar()
        {
            _statusLabel.Text = "준비됨";
            _status.Items.Add(_statusLabel);
            _status.Dock = DockStyle.Bottom;
        }

        private static string LogsFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Win11Debloat", "Logs"));
            }
        }

        // ------------------------------------------------------------------
        // 기능 탭
        // ------------------------------------------------------------------
        private void BuildCategoryList()
        {
            _categoryList.Items.Add("전체");
            foreach (CategoryInfo category in _catalog.Categories)
            {
                _categoryList.Items.Add(category.Label);
            }
            _categoryList.SelectedIndex = 0;
        }

        /// <summary>현재 분류/검색어에 맞춰 기능 패널을 다시 그린다.</summary>
        private void RebuildFeaturePanel()
        {
            _updatingPresets = true;
            try
            {
                _featurePanel.SuspendLayout();
                _featurePanel.Controls.Clear();

                string categoryName = _categoryList.SelectedItem as string;
                string filter = (_featureSearch.Text ?? string.Empty).Trim();
                bool useFilter = filter.Length > 0;

                CategoryInfo single = null;
                if (!useFilter && !string.IsNullOrEmpty(categoryName) && categoryName != "전체")
                {
                    foreach (CategoryInfo category in _catalog.Categories)
                    {
                        if (category.Label == categoryName) { single = category; break; }
                    }
                }

                int width = Math.Max(320, _featureHost.ClientSize.Width - 26);
                _lastHostWidth = _featureHost.ClientSize.Width;
                int visible = 0;

                foreach (CategoryInfo category in _catalog.Categories)
                {
                    if (single != null && category.Id != single.Id) continue;

                    List<FeatureInfo> features = new List<FeatureInfo>();
                    foreach (FeatureInfo feature in _catalog.Features)
                    {
                        if (_catalog.GroupMemberFeatureIds.Contains(feature.Id)) continue;
                        if (string.IsNullOrEmpty(feature.CategoryName)) continue;
                        if (feature.CategoryName != category.Name) continue;
                        if (useFilter && !Matches(feature.Label, filter) && !Matches(feature.Id, filter)) continue;
                        features.Add(feature);
                    }

                    List<GroupInfo> groups = new List<GroupInfo>();
                    foreach (GroupInfo group in _catalog.Groups)
                    {
                        if (group.CategoryName != category.Name) continue;
                        if (useFilter && !Matches(group.Label, filter) && !GroupMatches(group, filter)) continue;
                        groups.Add(group);
                    }

                    if (features.Count == 0 && groups.Count == 0) continue;

                    GroupBox box = new GroupBox
                    {
                        Text = category.Label,
                        Width = width,
                        Height = 40,
                        Margin = new Padding(2)
                    };

                    int y = 24;

                    foreach (FeatureInfo feature in features)
                    {
                        CheckBox check = new CheckBox
                        {
                            Text = feature.Label,
                            AutoSize = true,
                            Location = new Point(14, y),
                            Tag = feature.Id
                        };
                        if (!string.IsNullOrEmpty(feature.ToolTip))
                        {
                            ToolTip toolTip = new ToolTip();
                            toolTip.SetToolTip(check, feature.ToolTip);
                        }
                        CheckBox existing;
                        if (_featureBoxes.TryGetValue(feature.Id, out existing))
                        {
                            check.Checked = existing.Checked;
                        }
                        check.CheckedChanged += (s, e) => OnFeatureToggled();
                        box.Controls.Add(check);
                        _featureBoxes[feature.Id] = check;
                        y += 24;
                        visible++;
                    }

                    foreach (GroupInfo group in groups)
                    {
                        // 그룹마다 별도 컨테이너를 써야 라디오 선택이 서로 간_IRQ하지 않는다.
                        Panel groupPanel = new Panel
                        {
                            Location = new Point(18, y),
                            Width = width - 40,
                            Height = 30 + group.Values.Count * 22
                        };

                        Label groupLabel = new Label
                        {
                            Text = group.Label,
                            AutoSize = true,
                            Location = new Point(0, 0),
                            Font = new Font(Font, FontStyle.Bold)
                        };
                        if (!string.IsNullOrEmpty(group.ToolTip))
                        {
                            ToolTip toolTip = new ToolTip();
                            toolTip.SetToolTip(groupLabel, group.ToolTip);
                        }
                        groupPanel.Controls.Add(groupLabel);

                        // 선택 상태는 UI 와 분리해서 보관한다.
                        // 빈 문자열은 '변경 없음' 을 뜻한다.
                        string selectedId;
                        if (!_groupSelection.TryGetValue(group.Id, out selectedId)) selectedId = NoChange;
                        _groupSelection[group.Id] = selectedId;

                        int ry = 22;
                        RadioButton selectedRadio = null;

                        // upstream GUI 와 동일하게 '변경 없음' 을 기본 선택으로 둔다.
                        RadioButton noChange = new RadioButton
                        {
                            Text = "변경 없음 (현재 상태 유지)",
                            AutoSize = true,
                            Location = new Point(16, ry),
                            Tag = NoChange
                        };
                        noChange.CheckedChanged += (s, e) => OnGroupRadioChanged(group, s);
                        groupPanel.Controls.Add(noChange);
                        if (selectedId == NoChange) selectedRadio = noChange;
                        ry += 22;

                        foreach (GroupValueInfo value in group.Values)
                        {
                            RadioButton radio = new RadioButton
                            {
                                Text = value.Label,
                                AutoSize = true,
                                Location = new Point(16, ry),
                                Tag = value.FeatureId
                            };
                            radio.CheckedChanged += (s, e) => OnGroupRadioChanged(group, s);
                            groupPanel.Controls.Add(radio);
                            if (value.FeatureId == selectedId) selectedRadio = radio;
                            ry += 22;
                            visible++;
                        }

                        // 마지막에 선택 상태를 확정한다.
                        if (selectedRadio != null) selectedRadio.Checked = true;
                        _groupRadios[group.Id] = selectedRadio;

                        box.Controls.Add(groupPanel);
                        y += groupPanel.Height + 10;
                    }

                    box.Height = y + 8;
                    _featurePanel.Controls.Add(box);
                }

                _visibleFeatureCount = visible;
                _featurePanel.ResumeLayout();
            }
            finally
            {
                _updatingPresets = false;
            }

            UpdateSummary();
        }

        private static bool Matches(string text, string filter)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private bool GroupMatches(GroupInfo group, string filter)
        {
            foreach (GroupValueInfo value in group.Values)
            {
                if (Matches(value.Label, filter)) return true;
            }
            return false;
        }

        private void OnGroupRadioChanged(GroupInfo group, object sender)
        {
            RadioButton changed = sender as RadioButton;
            if (changed == null || !changed.Checked) return;
            string featureId = changed.Tag as string;
            _groupSelection[group.Id] = featureId ?? NoChange;
            OnFeatureToggled();
        }

        private void OnFeatureToggled()
        {
            if (_updatingPresets) return;
            UpdateSummary();
        }

        private void SetAllFeatures(bool value)
        {
            _updatingPresets = true;
            try
            {
                foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
                {
                    pair.Value.Checked = value;
                }
            }
            finally
            {
                _updatingPresets = false;
            }
            UpdateSummary();
        }

        // ------------------------------------------------------------------
        // 앱 제거 탭
        // ------------------------------------------------------------------
        private void RefreshAppList()
        {
            HashSet<string> checkedIds = new HashSet<string>(GetCheckedAppIds(), StringComparer.OrdinalIgnoreCase);

            string filter = (_appSearch.Text ?? string.Empty).Trim();
            _updatingAppList = true;
            _appList.BeginUpdate();
            try
            {
                _appList.Items.Clear();
                foreach (AppInfo app in _catalog.Apps)
                {
                    if (filter.Length > 0 &&
                        !Matches(app.FriendlyName, filter) &&
                        !Matches(app.Id, filter) &&
                        !Matches(app.Description, filter))
                    {
                        continue;
                    }

                    ListViewItem item = new ListViewItem(app.FriendlyName);
                    item.SubItems.Add(app.Description);
                    item.SubItems.Add(app.SelectedByDefault ? "기본" : string.Empty);
                    item.SubItems.Add(app.RemovalMethod);
                    item.Tag = app;
                    item.Checked = checkedIds.Contains(app.Id);
                    item.ToolTipText = app.Description.Length > 0
                        ? app.FriendlyName + " - " + app.Description
                        : app.FriendlyName;
                    _appList.Items.Add(item);
                }
            }
            finally
            {
                _appList.EndUpdate();
                _updatingAppList = false;
            }
            UpdateSummary();
        }

        private void SetAllApps(bool value)
        {
            _appList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in _appList.Items)
                {
                    item.Checked = value;
                }
            }
            finally
            {
                _appList.EndUpdate();
            }
            UpdateSummary();
        }

        private void SelectApps(IEnumerable<string> appIds)
        {
            HashSet<string> wanted = new HashSet<string>(appIds, StringComparer.OrdinalIgnoreCase);
            _appList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in _appList.Items)
                {
                    AppInfo app = item.Tag as AppInfo;
                    item.Checked = app != null && wanted.Contains(app.Id);
                }
            }
            finally
            {
                _appList.EndUpdate();
            }
            UpdateSummary();
        }

        private void SelectPresetApps(int index)
        {
            if (index < 0 || index >= _catalog.Presets.Count) return;
            SelectApps(_catalog.Presets[index].AppIds);
        }

        // ------------------------------------------------------------------
        // 프리셋
        // ------------------------------------------------------------------
        private List<string> GetCategoryFeatureIds(string categoryId)
        {
            List<string> result = new List<string>();
            CategoryInfo category = _catalog.Category(categoryId);
            if (category == null) return result;

            foreach (FeatureInfo feature in _catalog.Features)
            {
                if (feature.CategoryName != category.Name) continue;
                if (_catalog.GroupMemberFeatureIds.Contains(feature.Id)) continue;
                result.Add(feature.Id);
            }
            return result;
        }

        private void ApplyPreset(CheckBox source, IList<string> featureIds)
        {
            if (_updatingPresets) return;
            _updatingPresets = true;
            try
            {
                bool target = source.Checked;
                HashSet<string> wanted = new HashSet<string>(featureIds, StringComparer.OrdinalIgnoreCase);

                foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
                {
                    pair.Value.Checked = wanted.Contains(pair.Key);
                }

                if (!target)
                {
                    foreach (CheckBox other in new[] { _presetDefault, _presetPrivacy, _presetAi, _presetLastUsed })
                    {
                        if (!ReferenceEquals(other, source)) other.Checked = false;
                    }
                }
            }
            finally
            {
                _updatingPresets = false;
            }
            UpdateSummary();
        }

        private void ApplyDefaultSelections()
        {
            _updatingPresets = true;
            try
            {
                HashSet<string> wanted = new HashSet<string>(_catalog.DefaultFeatureIds, StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
                {
                    pair.Value.Checked = wanted.Contains(pair.Key);
                }
            }
            finally
            {
                _updatingPresets = false;
            }
        }

        private void ResetSelections()
        {
            _updatingPresets = true;
            try
            {
                foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
                {
                    pair.Value.Checked = false;
                }
                foreach (CheckBox preset in new[] { _presetDefault, _presetPrivacy, _presetAi, _presetLastUsed })
                {
                    preset.Checked = false;
                }
                _chkRestorePoint.Checked = false;
                _chkSkipExplorer.Checked = false;
                _chkSysprep.Checked = false;
                _txtUser.Text = string.Empty;
                SetAllApps(false);
            }
            finally
            {
                _updatingPresets = false;
            }
            UpdateSummary();
        }

        // ------------------------------------------------------------------
        // 적용
        // ------------------------------------------------------------------
        private List<string> BuildArguments()
        {
            List<string> args = new List<string>();
            args.Add("-Language");
            args.Add("'" + Catalog.LanguageCode + "'");
            args.Add("-Silent");

            foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
            {
                if (pair.Value.Checked) args.Add("-" + pair.Key);
            }

            foreach (GroupInfo group in _catalog.Groups)
            {
                string featureId;
                if (!_groupSelection.TryGetValue(group.Id, out featureId)) continue;
                if (string.IsNullOrEmpty(featureId)) continue;

                GroupValueInfo value = null;
                foreach (GroupValueInfo candidate in group.Values)
                {
                    if (candidate.FeatureId == featureId) { value = candidate; break; }
                }
                // '변경 없음' 이면 스위치를 만들지 않는다.
                // 사용자가 명시적으로 고른 옵션은 '(기본값)' 여도 그대로 적용한다.
                if (value == null) continue;
                args.Add("-" + value.FeatureId);
            }

            List<string> appIds = GetCheckedAppIds();
            if (appIds.Count > 0)
            {
                args.Add("-RemoveApps");
                args.Add("-Apps");
                args.Add("'" + string.Join(",", appIds.ToArray()) + "'");
            }

            if (_chkRestorePoint.Checked) args.Add("-CreateRestorePoint");
            if (!_chkRegistryBackup.Checked) args.Add("-SkipRegistryBackup");
            if (_chkSkipExplorer.Checked) args.Add("-SkipExplorerRestart");
            if (_chkSysprep.Checked) args.Add("-Sysprep");
            if (_chkGamingApps.Checked) args.Add("-RemoveGamingApps");
            if (_chkHpApps.Checked) args.Add("-RemoveHPApps");
            if (_chkForceEdge.Checked) args.Add("-ForceRemoveEdge");

            if (_chkStartLayout.Checked)
            {
                string layout = (_txtStartLayout.Text ?? string.Empty).Trim();
                if (layout.Length == 0)
                {
                    layout = Path.Combine(_payloadRoot, Path.Combine("Assets", Path.Combine("Start", "start2.bin")));
                }
                if (_chkStartLayoutAllUsers.Checked) args.Add("-ReplaceStartAllUsers");
                else args.Add("-ReplaceStart");
                args.Add("'" + layout.Replace("'", "''") + "'");
            }

            string user = (_txtUser.Text ?? string.Empty).Trim();
            if (user.Length > 0)
            {
                args.Add("-User");
                args.Add("'" + user.Replace("'", "''") + "'");
            }

            return args;
        }

        /// <summary>선택한 앱들의 전체 패키지 ID (다중 패키지 앱은 모두 포함).</summary>
        private List<string> GetCheckedAppIds()
        {
            List<string> result = new List<string>();
            foreach (ListViewItem item in _appList.Items)
            {
                if (item == null || !item.Checked) continue;
                AppInfo app = item.Tag as AppInfo;
                if (app == null || app.AppIds == null) continue;
                foreach (string id in app.AppIds)
                {
                    if (!result.Contains(id)) result.Add(id);
                }
            }
            return result;
        }

        /// <summary>선택한 앱 항목 수.</summary>
        private int GetCheckedAppCount()
        {
            int count = 0;
            foreach (ListViewItem item in _appList.Items)
            {
                if (item != null && item.Checked) count++;
            }
            return count;
        }

        private int CountSelectedFeatures()
        {
            int count = 0;
            foreach (KeyValuePair<string, CheckBox> pair in _featureBoxes)
            {
                if (pair.Value.Checked) count++;
            }
            foreach (GroupInfo group in _catalog.Groups)
            {
                string featureId;
                if (!_groupSelection.TryGetValue(group.Id, out featureId)) continue;
                if (string.IsNullOrEmpty(featureId)) continue;

                GroupValueInfo value = null;
                foreach (GroupValueInfo candidate in group.Values)
                {
                    if (candidate.FeatureId == featureId) { value = candidate; break; }
                }
                if (value != null) count++;
            }
            return count;
        }

        private void UpdateSummary()
        {
            int featureCount = CountSelectedFeatures();
            int appCount = GetCheckedAppCount();
            _summaryLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "선택된 기능 {0}개  ·  제거할 앱 {1}개  ·  복원 지점 {2}",
                featureCount,
                appCount,
                _chkRestorePoint.Checked ? "사용" : "사용 안 함");
            _featureCount.Text = string.Format(CultureInfo.CurrentCulture, "표시 중인 항목: {0}개", _visibleFeatureCount);
            _appCount.Text = string.Format(CultureInfo.CurrentCulture, "앱 {0}개 / 전체 {1}개", _appList.Items.Count, _catalog.Apps.Count);

            List<string> args = BuildArguments();
            _txtPreview.Text = string.Join(" ", args.ToArray());
            _previewLabel.Text = args.Count > 0
                ? "powershell.exe " + string.Join(" ", args.ToArray())
                : "선택된 항목이 없습니다.";
        }

        private void StartApply()
        {
            if (_previewMode)
            {
                MessageBox.Show(this, "미리보기 모드에서는 변경 사항을 적용할 수 없습니다.",
                    "미리보기 모드", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_runner.IsRunning)
            {
                MessageBox.Show(this, "이미 작업이 실행 중입니다.", "Win11Debloat 한국어 도구",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int featureCount = CountSelectedFeatures();
            int appCount = GetCheckedAppCount();
            if (featureCount == 0 && appCount == 0 && !_chkRestorePoint.Checked)
            {
                MessageBox.Show(this,
                    "적용할 항목이 없습니다.\n'기능' 또는 '앱 제거' 탭에서 항목을 선택한 뒤 다시 시도해 주세요.",
                    "선택 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string message = string.Format(
                CultureInfo.CurrentCulture,
                "다음 변경 사항을 적용합니다.\n\n· 기능 {0}개\n· 제거할 앱 {1}개\n· 복원 지점 만들기: {2}\n· 레지스트리 백업: {3}\n\n계속하려면 [확인]을 누르세요.",
                featureCount,
                appCount,
                _chkRestorePoint.Checked ? "예" : "아니오",
                _chkRegistryBackup.Checked ? "예" : "아니오");

            DialogResult answer = MessageBox.Show(this, message, "변경 사항 적용 확인",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;

            List<string> args = BuildArguments();
            string scriptPath = Path.Combine(_payloadRoot, "Win11Debloat.ps1");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this, "내장 스크립트를 찾을 수 없습니다:\n" + scriptPath,
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _logBox.Clear();
            AppendLog("=== Win11Debloat 작업을 시작합니다 ===", false);
            AppendLog("스크립트: " + scriptPath, false);
            AppendLog("", false);

            _applyButton.Enabled = false;
            _cancelButton.Enabled = true;
            _statusLabel.Text = "작업 중...";

            try
            {
                _runner.Start(scriptPath, args, _payloadRoot);
                _tabs.SelectedIndex = 3;
            }
            catch (Exception ex)
            {
                _applyButton.Enabled = true;
                _cancelButton.Enabled = false;
                _statusLabel.Text = "실행 실패";
                MessageBox.Show(this, "작업을 시작하지 못했습니다.\n\n" + ex.Message,
                    "실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CancelApply()
        {
            if (!_runner.IsRunning) return;
            DialogResult answer = MessageBox.Show(this,
                "실행 중인 작업을 중단하시겠습니까?\n중간까지 적용된 변경 사항은 되돌아오지 않습니다.",
                "작업 중단", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;

            _runner.Kill();
            _applyButton.Enabled = true;
            _cancelButton.Enabled = false;
            _statusLabel.Text = "작업이 중단되었습니다";
            AppendLog("=== 사용자에 의해 작업이 중단되었습니다 ===", true);
        }

        private void OnRunnerOutput(string text, bool isError)
        {
            try
            {
                if (IsDisposed) return;
                BeginInvoke(new Action<string, bool>(AppendLog), text, isError);
            }
            catch (ObjectDisposedException)
            {
                // 창이 이미 닫혔다.
            }
            catch (InvalidOperationException)
            {
                // 창이 이미 닫혔다.
            }
        }

        private void AppendLog(string text, bool isError)
        {
            if (_logBox.TextLength > 0) _logBox.AppendText(Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionLength = 0;
            _logBox.SelectionColor = isError ? Color.Firebrick : Color.Black;
            _logBox.AppendText(text);
            _logBox.ScrollToCaret();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_runner.IsRunning)
            {
                _runner.Kill();
            }
            base.OnFormClosed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
        }

        // ------------------------------------------------------------------
        // 보조 동작
        // ------------------------------------------------------------------
        private void PickStartLayoutFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "시작 메뉴 레이아웃(.bin) 파일 선택";
                dialog.Filter = "시작 메뉴 레이아웃 (*.bin)|*.bin|모든 파일 (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _txtStartLayout.Text = dialog.FileName;
                    _chkStartLayout.Checked = true;
                    UpdateSummary();
                }
            }
        }

        private void CopyLog()
        {
            if (_logBox.TextLength == 0) return;
            try
            {
                Clipboard.SetText(_logBox.Text);
                _statusLabel.Text = "로그를 클립보드에 복사했습니다.";
            }
            catch (Exception)
            {
                _statusLabel.Text = "클립보드 복사에 실패했습니다.";
            }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("폴더를 열 수 없습니다: " + ex.Message, "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RelaunchElevated()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(info);
                Application.Exit();
            }
            catch (Exception)
            {
                MessageBox.Show(this, "관리자 권한으로 다시 실행하지 못했습니다.",
                    "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowLanguageInfo()
        {
            MessageBox.Show(this,
                "인터페이스 언어 : 한국어 (ko-KR)\n" +
                "스크립트 언어   : ko-KR\n\n" +
                "기능·앱 이름·설명 텍스트는 win11debloat 저장소의\n" +
                "Config\\Languages\\ko-KR 폴더에서 직접 읽어옵니다.",
                "언어 설정 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowUsage()
        {
            MessageBox.Show(this,
                "1) '기본' 탭에서 프리셋을 고르거나 실행 옵션을 설정합니다.\n" +
                "2) '기능' 탭에서 원하는 항목만 선택합니다.\n" +
                "3) '앱 제거' 탭에서 제거할 앱을 선택합니다.\n" +
                "4) 하단의 [적용하기] 버튼을 누릅니다.\n" +
                "5) '로그' 탭에서 진행 상황과 결과를 확인합니다.\n\n" +
                "· 그룹(라디오) 항목의 '(기본값)' 선택은 시스템 기본값을 유지하므로 아무것도 적용하지 않습니다.\n" +
                "· '제거 방식'이 Appx 가 아닌 앱은 winget 이 필요합니다.",
                "사용법 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "Win11Debloat 한국어 도구\n" +
                "버전 1.0\n\n" +
                "원본 프로젝트 : https://github.com/raphire/win11debloat\n" +
                "라이선스      : MIT\n\n" +
                "이 프로그램은 win11debloat 소스를 그대로 사용하며,\n" +
                "한국어 GUI 래퍼와 exe 실행 파일을 제공합니다.",
                "프로그램 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal static Font CreateKoreanFont(float size)
        {
            string[] candidates = { "Malgun Gothic", "맑은 고딕", "Yu Gothic UI", "Meiryo UI", "Segoe UI" };
            foreach (string name in candidates)
            {
                try
                {
                    FontFamily family = new FontFamily(name);
                    return new Font(family, size);
                }
                catch (ArgumentException)
                {
                    // 설치되어 있지 않으면 다음 후보를 시도한다.
                }
            }
            return new Font(FontFamily.GenericSansSerif, size);
        }
    }
}
