// ════════════════════════════════════════════════════════════════════
// KgViewModel.Builder.cs (v2.7.12)
//
// v2.4 (본책) → v2.7.12 누적 변경사항을 모두 포함한 통째 교체본:
//   * v2.7.5  : 'none' 빌더 가드 추가 (IsNoneBuilderDomain 헬퍼)
//   * v2.7.6  : 사용자 cs_file 도메인 — BuilderScript 절대경로 지원,
//              KgBuilderRunner.LocateScript(KgDomain) 메타 기반 호출
//   * v2.7.7  : 사용자 python_engine_folder — IsFolderBasedDomain 헬퍼,
//              폴더 기반 src 처리
//   * v2.7.8  : 2단계 빌드 일반화 (사용자 python_engine_folder 도 dump→build),
//              LocateDumpScriptForDomain 헬퍼, OutputGeneratedCsPath(KgDomain)
//   * v2.7.12 : src 검증 강화 — 부재 시 자동 폴백 다이얼로그 제거,
//              빌드 거부 + [경로변경] 안내,
//              settings 와 메모리 불일치 감지,
//              PathEquals 헬퍼
//
// 사용 조건:
//   1) 기존 KgViewModel.cs 의 클래스 선언이 'public partial class KgViewModel'
//      이어야 함 (이미 그렇게 되어있음).
//   2) Services/KgSettingsService.cs 도 v2.7.12 로 교체되어 있어야 함
//      (KeyForDomain 의 default 가 동적 키, KeyForDomainDynamic 별칭, KeyForLastImport).
//   3) KgViewModel.Manage.cs 가 있다면 거기서 _domainSvc 를 정의/주입.
//      없어도 빌트인 5종은 정상 동작 (사용자 도메인 기능만 비활성).
//   4) Models/KgDomain.cs 와 Services/KgDomainService.cs 는 선택적 의존.
// ════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.ViewModels
{
    public partial class KgViewModel
    {
        // ── 의존성 (AttachBuilder 로 주입) ─────────────
        private KgSettingsService? _settings;
        // v4.1: KG 빌드 실행은 SPILab Core(IKgBuilder) 경유. KgBuilderRunner 의
        //       인스턴스 _runner 는 더 이상 직접 호출하지 않으므로 제거했다.
        //       (KgBuilderRunner 의 static 경로 헬퍼는 그대로 사용 — IP 아님)

        // ── 상태 프로퍼티 ─────────────────────────────
        private bool _isBuilding;
        public bool IsBuilding
        {
            get => _isBuilding;
            private set
            {
                if (Set(ref _isBuilding, value))
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _buildStatus = "";
        public string BuildStatus
        {
            get => _buildStatus;
            private set { Set(ref _buildStatus, value); }
        }

        private string? _builderSrcPath;
        /// <summary>현재 등록된 --src 경로 (도메인에 따라 자동 전환됨)</summary>
        public string? BuilderSrcPath
        {
            get => _builderSrcPath;
            private set { Set(ref _builderSrcPath, value); }
        }

        private string? _pythonExePath;
        /// <summary>현재 등록된 Python 실행파일 절대경로 (양 도메인 공유)</summary>
        public string? PythonExePath
        {
            get => _pythonExePath;
            private set { Set(ref _pythonExePath, value); }
        }

        // ── 도메인 ────────────────────────────────────
        private string _selectedDomain = KnowledgeGraphService.DOMAIN_CS;
        /// <summary>현재 선택된 도메인. KgTab.xaml 의 도메인 콤보가 바인딩.</summary>
        public string SelectedDomain
        {
            get => _selectedDomain;
            set
            {
                if (string.IsNullOrEmpty(value)) value = KnowledgeGraphService.DOMAIN_CS;
                if (Set(ref _selectedDomain, value))
                {
                    // 도메인 전환 시 src 경로 다시 로드
                    if (_settings != null)
                        BuilderSrcPath = _settings.Get(KgSettingsService.KeyForDomain(value));
                    OnPropertyChanged(nameof(SelectedDomainLabel));
                    DomainChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>UI 표시용 라벨.</summary>
        public string SelectedDomainLabel
        {
            get
            {
                // 빌트인 5종은 고정 라벨
                switch (SelectedDomain)
                {
                    case KnowledgeGraphService.DOMAIN_PHOTO:    return "SimPhoto — 포토리소그래피";
                    case KnowledgeGraphService.DOMAIN_CMP:      return "SimCMP — CMP 공정";
                    case KnowledgeGraphService.DOMAIN_ETCH:     return "SimEtch — 식각 공정";
                    case KnowledgeGraphService.DOMAIN_THINFILM: return "SimThinFilm — 박막증착";
                    case KnowledgeGraphService.DOMAIN_CS:       return "SimCS — GaN/SiC";
                }
                // 사용자 도메인 — _domainSvc 에서 라벨 조회
                var meta = GetCurrentDomainMeta();
                return GetLabel(meta) ?? SelectedDomain;
            }
        }

        public event EventHandler? DomainChanged;

        // ── 명령 ─────────────────────────────────────
        private RelayCommand? _buildCmd;
        public ICommand BuildCommand =>
            _buildCmd ??= new RelayCommand(_ => _ = DoBuildAsync(), _ => !IsBuilding);

        private RelayCommand? _changeSrcCmd;
        public ICommand ChangeBuilderSrcCommand =>
            _changeSrcCmd ??= new RelayCommand(_ => DoChangeSrc(), _ => !IsBuilding);

        // ── 외부에서 1회 호출 ─────────────────────────
        public void AttachBuilder(KgSettingsService settings)
        {
            _settings = settings;
            BuilderSrcPath = settings.Get(KgSettingsService.KeyForDomain(_selectedDomain))
                          ?? (_selectedDomain == KnowledgeGraphService.DOMAIN_CS
                                ? settings.Get(KgSettingsService.KEY_BUILDER_SRC)
                                : null);
            PythonExePath  = settings.Get(KgSettingsService.KEY_PYTHON_EXE);
        }

        // ── 경로 변경 ([경로변경] 버튼) ─────────────────
        private void DoChangeSrc()
        {
            // 'none' 도메인 가드
            if (IsNoneBuilderDomain())
            {
                MessageBox.Show(
                    "현재 도메인은 빌더 종류가 '없음' 으로 설정되어 있어\n" +
                    "엔진 소스 경로를 변경할 수 없습니다.\n\n" +
                    "이 도메인은 [KG 임포트] 버튼으로 외부 TTL/JSON 파일을\n" +
                    "직접 가져와 사용하세요.",
                    "빌더 없는 도메인",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 폴더 기반 (photo 빌트인 + 사용자 python_engine_folder) vs 파일 기반 (cs_file 등)
            var path = IsFolderBasedDomain()
                ? AskUserForPhotoFolder()
                : AskUserForCsFile();
            if (path == null) return;

            _settings?.Set(KgSettingsService.KeyForDomain(SelectedDomain), path);
            BuilderSrcPath = path;

            BuildStatus = IsFolderBasedDomain()
                ? "Python engine 폴더 등록 완료."
                : $"엔진 파일 경로 등록 완료 ({SelectedDomainLabel}).";
        }

        /// <summary>호환용 alias.</summary>
        private string? AskUserForSrcPath()
            => IsFolderBasedDomain()
                ? AskUserForPhotoFolder()
                : AskUserForCsFile();

        private string? AskUserForCsFile()
        {
            string title = SelectedDomain switch
            {
                KnowledgeGraphService.DOMAIN_CMP =>
                    "레이판 Sim CMP 엔진 파일 선택 — CmpPhysicsEngine.cs 를 선택하세요",
                KnowledgeGraphService.DOMAIN_ETCH =>
                    "레이판 Sim Etch 엔진 파일 선택 — EtchPhysicsEngine.cs 를 선택하세요",
                KnowledgeGraphService.DOMAIN_THINFILM =>
                    "레이판 Sim ThinFilm 엔진 파일 선택 — ThinFilmPhysicsEngine.cs 를 선택하세요",
                KnowledgeGraphService.DOMAIN_CS =>
                    "레이판 Sim CS 엔진 파일 선택 — CSPhysicsEngine.cs 를 선택하세요",
                _ =>
                    $"[{SelectedDomainLabel}] 엔진 파일 (.cs) 선택",
            };
            var dlg = new OpenFileDialog
            {
                Title  = title,
                Filter = "C# 소스 파일 (*.cs)|*.cs|모든 파일 (*.*)|*.*",
            };
            if (!string.IsNullOrEmpty(BuilderSrcPath) && File.Exists(BuilderSrcPath))
                dlg.InitialDirectory = Path.GetDirectoryName(BuilderSrcPath);
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private string? AskUserForPhotoFolder()
        {
            try
            {
                var fdlg = new OpenFolderDialog
                {
                    Title = SelectedDomain == KnowledgeGraphService.DOMAIN_PHOTO
                        ? "레이판 Sim Photo 폴더 선택 — python_engine 폴더를 선택하세요"
                        : $"[{SelectedDomainLabel}] Python engine 폴더 선택",
                };
                if (!string.IsNullOrEmpty(BuilderSrcPath) && Directory.Exists(BuilderSrcPath))
                    fdlg.InitialDirectory = BuilderSrcPath;
                if (fdlg.ShowDialog() == true)
                    return fdlg.FolderName;
                return null;
            }
            catch (TypeLoadException)
            {
                var dlg = new OpenFileDialog
                {
                    Title  = "Python engine 폴더 안의 임의 .py 파일을 선택하세요",
                    Filter = "Python 파일 (*.py)|*.py|모든 파일 (*.*)|*.*",
                };
                if (!string.IsNullOrEmpty(BuilderSrcPath) && Directory.Exists(BuilderSrcPath))
                    dlg.InitialDirectory = BuilderSrcPath;
                if (dlg.ShowDialog() == true)
                    return Path.GetDirectoryName(dlg.FileName);
                return null;
            }
        }

        private string? AskUserForPythonPath()
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Python 실행파일 선택 — python.exe (예: 아나콘다 가상환경 python.exe)",
                Filter = "Python 실행파일 (python.exe)|python.exe|실행파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
            };
            if (!string.IsNullOrEmpty(PythonExePath) && File.Exists(PythonExePath))
            {
                dlg.InitialDirectory = Path.GetDirectoryName(PythonExePath);
            }
            else
            {
                var candidates = new[]
                {
                    Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? "", "anaconda3", "envs"),
                    Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "anaconda3", "envs"),
                    Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "", "miniconda3", "envs"),
                };
                foreach (var c in candidates)
                {
                    if (Directory.Exists(c)) { dlg.InitialDirectory = c; break; }
                }
            }
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ════════════════════════════════════════════════════════════
        //  DoBuildAsync — 빌드 실행
        //  v2.7.12 의 src 검증 강화 + v2.7.5~v2.7.8 의 도메인 메타 기반 분기 통합
        // ════════════════════════════════════════════════════════════
        private async Task DoBuildAsync()
        {
            if (_settings == null)
            {
                MessageBox.Show("빌더 설정이 부착되지 않았습니다 (AttachBuilder 미호출).",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 'none' 도메인 가드
            if (IsNoneBuilderDomain())
            {
                MessageBox.Show(
                    "현재 도메인은 빌더 종류가 '없음' 으로 설정되어 있어\n" +
                    "KG 빌드 기능을 사용할 수 없습니다.\n\n" +
                    "이 도메인은 외부에서 만든 TTL/JSON 을 [KG 임포트] 로\n" +
                    "가져오는 방식으로만 데이터를 채울 수 있습니다.",
                    "빌더 없는 도메인",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1) build 스크립트 위치 확인 — 도메인 메타 기반 우선, 없으면 빌트인 코드 폴백
            string? script = LocateBuildScriptForCurrent();
            if (script == null)
            {
                ShowScriptNotFoundError();
                return;
            }

            // 2) ★ v2.7.12: src 경로 검증 강화 — 자동 폴백 다이얼로그 제거
            bool isFolderBased = IsFolderBasedDomain();
            var src = BuilderSrcPath;

            // 2-a) src 미등록
            if (string.IsNullOrEmpty(src))
            {
                MessageBox.Show(
                    $"[{SelectedDomainLabel}] 도메인의 엔진 경로가 등록되지 않았습니다.\n\n" +
                    "[경로변경] 버튼을 눌러 " +
                    (isFolderBased ? "Python engine 폴더" : "C# 엔진 파일 (.cs)") +
                    " 을 먼저 지정해 주세요.",
                    "엔진 경로 미등록",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2-b) src 존재 여부 검증 — 부재 시 즉시 거부
            bool srcExists = isFolderBased ? Directory.Exists(src) : File.Exists(src);
            if (!srcExists)
            {
                MessageBox.Show(
                    $"[{SelectedDomainLabel}] 등록된 엔진 경로의 " +
                    (isFolderBased ? "폴더" : "파일") +
                    " 이 더 이상 존재하지 않습니다.\n\n" +
                    $"등록된 경로:\n  {src}\n\n" +
                    "원본이 이동/이름변경/삭제 되었습니다.\n" +
                    "이 상태에서는 빌드가 다른 파일을 사용하게 될 위험이 있어 진행을 거부합니다.\n" +
                    "[경로변경] 버튼을 눌러 새 경로를 지정해 주세요.",
                    "엔진 경로 부재 — 빌드 거부",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2-c) settings 와 메모리 불일치 검사 — 도메인 콤보 전환 직후 등의 보정
            var savedSrc = _settings.Get(KgSettingsService.KeyForDomain(SelectedDomain));
            if (!string.IsNullOrEmpty(savedSrc) && !PathEquals(savedSrc, src))
            {
                var ans = MessageBox.Show(
                    $"메모리상의 엔진 경로와 저장된 경로가 다릅니다.\n\n" +
                    $"메모리: {src}\n" +
                    $"저장됨: {savedSrc}\n\n" +
                    "저장된 경로를 사용하시겠습니까?\n" +
                    "(아니오 = 메모리 경로 사용 + 저장됨에 덮어쓰기)",
                    "경로 불일치 감지",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans == MessageBoxResult.Yes)
                {
                    src = savedSrc;
                    BuilderSrcPath = savedSrc;
                    bool reOk = isFolderBased ? Directory.Exists(src) : File.Exists(src);
                    if (!reOk)
                    {
                        MessageBox.Show(
                            $"저장된 경로의 파일/폴더도 존재하지 않습니다:\n{src}\n\n" +
                            "[경로변경] 으로 새 경로를 지정하세요.",
                            "엔진 경로 부재", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    _settings.Set(KgSettingsService.KeyForDomain(SelectedDomain), src);
                }
            }

            // 3) Python 경로 확인 — 옛 동작 그대로 (자동 다이얼로그 OK — 거기서 잘못 골라도 빌드 동작에는 큰 차이 없음)
            var py = PythonExePath;
            if (string.IsNullOrEmpty(py) || !File.Exists(py))
            {
                MessageBox.Show(
                    "Python 실행파일이 등록되지 않았거나 존재하지 않습니다.\n" +
                    "다음 화면에서 사용할 python.exe 를 직접 선택해 주세요.\n\n" +
                    "예: C:\\ProgramData\\anaconda3\\envs\\<환경이름>\\python.exe",
                    "Python 실행파일 선택 필요",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                py = AskUserForPythonPath();
                if (py == null) return;
                PythonExePath = py;
                _settings.Set(KgSettingsService.KEY_PYTHON_EXE, py);
            }

            // 4) 빌드 실행
            IsBuilding = true;
            BuildStatus = "빌드 실행 중...";
            BuildStatus = "빌드 실행 중...";

            // ── v4.1: 빌드를 SPILab Core(IKgBuilder) 경유로 실행 ──────────
            //   2단계(dump→build) 처리·Secure-Verify 게이트·감사 로그는
            //   PhysicsKgBuilder 내부에서 수행된다. Core 미탑재 시에는
            //   NullKgBuilder 가 Skipped 결과를 반환한다(앱은 정상 동작).
            //   기존 v4.0 의 KgBuilderRunner 실행 로직은 PhysicsKgBuilder
            //   안에서 그대로 재사용되므로 산출물·동작은 동일하다.
            BuildResult result;
            try
            {
                var progress = new Progress<string>(line =>
                    BuildStatus = line.Length > 120 ? line.Substring(0, 120) + "..." : line);

                bool needsTwoStage = IsTwoStageBuild();
                string? dumpScript = needsTwoStage ? LocateDumpScriptForCurrent() : null;

                if (needsTwoStage && dumpScript == null)
                {
                    IsBuilding = false;
                    MessageBox.Show(
                        $"[{SelectedDomainLabel}] 2단계 빌드에 필요한 Dump 스크립트를 찾을 수 없습니다.\n" +
                        "kg_builder/dump_*.py 가 있어야 합니다.",
                        "Dump 스크립트 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var request = new Core.Contracts.BuildRequest
                {
                    Domain         = SelectedDomain,
                    SourcePath     = src ?? "",
                    ScriptPath     = script,
                    DumpScriptPath = dumpScript,
                    PythonExe      = py,
                };

                var coreResult = await Core.Provider.CoreServices.KgBuilder
                    .BuildAsync(request, progress, CancellationToken.None)
                    .ConfigureAwait(true);

                // Core 미탑재/인증 실패 — 기능 비활성 안내 후 종료
                if (coreResult.Skipped)
                {
                    IsBuilding = false;
                    BuildStatus = "KG 빌드 비활성 (Core 미탑재)";
                    MessageBox.Show(
                        "KG 빌드 기능을 사용할 수 없습니다.\n\n" +
                        coreResult.StdErr + "\n\n" +
                        "이 배포본은 SPILab Core 가 분리된 외부용일 수 있습니다.\n" +
                        "자산 관리·백업·내보내기 등 다른 기능은 정상 사용 가능합니다.",
                        "SPILab Core 비활성", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 계약 BuildResult → 기존 Services.BuildResult 로 변환 (이후 코드 무변경)
                result = new BuildResult
                {
                    Success    = coreResult.Success,
                    ExitCode   = coreResult.ExitCode,
                    StdOut     = coreResult.StdOut,
                    StdErr     = coreResult.StdErr,
                    JsonPath   = coreResult.JsonPath,
                    TtlPath    = coreResult.TtlPath,
                    Elapsed    = coreResult.Elapsed,
                    PythonUsed = coreResult.PythonUsed,
                    ScriptPath = coreResult.ScriptPath,
                };
            }
            catch (Exception ex)
            {
                IsBuilding = false;
                BuildStatus = "빌드 예외 발생";
                MessageBox.Show("빌드 중 예외 발생:\n" + ex, "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                IsBuilding = false;
            }

            // 5) 결과 처리
            if (!result.Success)
            {
                BuildStatus = $"빌드 실패 (exit={result.ExitCode}, {result.Elapsed.TotalSeconds:F1}s)";
                MessageBox.Show(
                    $"KG 빌드 실패\n\n" +
                    $"종료코드: {result.ExitCode}\n" +
                    $"경과시간: {result.Elapsed.TotalSeconds:F1}초\n" +
                    $"Python: {result.PythonUsed}\n\n" +
                    $"--- stderr ---\n{Truncate(result.StdErr, 1500)}\n\n" +
                    $"--- stdout ---\n{Truncate(result.StdOut, 800)}",
                    "KG 빌드 실패", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BuildStatus = $"빌드 완료 ({result.Elapsed.TotalSeconds:F1}s)";

            var summary =
                $"KG 빌드 완료.\n\n" +
                $"산출물:\n  • {result.JsonPath}\n  • {result.TtlPath}\n\n" +
                $"경과시간: {result.Elapsed.TotalSeconds:F1}초\n\n" +
                $"빌더 출력:\n{Truncate(result.StdOut, 800)}\n\n" +
                $"지금 NowMoment KG 에 임포트하시겠습니까?";
            var importAns = MessageBox.Show(summary, "KG 빌드 성공",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (importAns == MessageBoxResult.Yes)
            {
                try
                {
                    var stats = _kg.ImportFromJson(result.JsonPath, SelectedDomain);
                    Reload();
                    BuildStatus = $"임포트 완료 — 노드 {stats.Nodes} · 엣지 {stats.Edges}";
                    // 사용자 도메인 last_import_path 갱신 (빌트인은 무해)
                    _settings.Set(KgSettingsService.KeyForLastImport(SelectedDomain), result.JsonPath);
                    MessageBox.Show(
                        $"KG 임포트 완료 ({SelectedDomainLabel}).\n\n노드: {stats.Nodes}\n엣지: {stats.Edges}",
                        "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    BuildStatus = "임포트 실패";
                    MessageBox.Show("임포트 실패:\n" + ex.Message, "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  헬퍼 메서드들
        // ════════════════════════════════════════════════════════════

        /// <summary>현재 도메인이 'none' 빌더인지.</summary>
        private bool IsNoneBuilderDomain()
        {
            return GetBuilderKind(GetCurrentDomainMeta()) == "none";
        }

        /// <summary>현재 도메인이 폴더 기반 src (--src 가 폴더) 인지.</summary>
        private bool IsFolderBasedDomain()
        {
            // 빌트인 photo
            if (SelectedDomain == KnowledgeGraphService.DOMAIN_PHOTO) return true;
            // 사용자 python_engine_folder
            return GetBuilderKind(GetCurrentDomainMeta()) == "python_engine_folder";
        }

        /// <summary>현재 도메인이 2단계 빌드(dump → build) 인지.</summary>
        private bool IsTwoStageBuild()
        {
            if (SelectedDomain == KnowledgeGraphService.DOMAIN_PHOTO) return true;
            return GetBuilderKind(GetCurrentDomainMeta()) == "python_engine_folder";
        }

        /// <summary>
        /// 현재 SelectedDomain 의 도메인 메타 (object) 조회 — reflection 으로 _domainSvc 사용.
        /// _domainSvc 가 부착되지 않았거나 빌트인 5종이면 null 반환 → 빌트인 코드 경로로 폴백.
        /// 반환 타입은 object — 호출 측이 reflection 으로 BuilderKind, BuilderScript, DumpScript, IsBuiltIn 등 프로퍼티 접근.
        /// 이 방식으로 KgDomain / KgDomainService 가 프로젝트에 없는 환경에서도 컴파일 가능.
        /// </summary>
        private object? GetCurrentDomainMeta()
        {
            var svc = TryGetDomainService();
            if (svc == null) return null;
            try
            {
                var mi = svc.GetType().GetMethod("Get", new[] { typeof(string) });
                return mi?.Invoke(svc, new object[] { SelectedDomain });
            }
            catch { return null; }
        }

        /// <summary>_domainSvc 필드를 reflection 으로 안전하게 가져옴 (없어도 OK).</summary>
        private object? TryGetDomainService()
        {
            try
            {
                var fld = GetType().GetField("_domainSvc",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                return fld?.GetValue(this);
            }
            catch { return null; }
        }

        /// <summary>도메인 메타에서 BuilderKind 문자열 가져오기.</summary>
        private static string? GetBuilderKind(object? meta)
            => meta == null ? null : TryGetStringProperty(meta, "BuilderKind");

        /// <summary>도메인 메타에서 BuilderScript 문자열 가져오기.</summary>
        private static string? GetBuilderScript(object? meta)
            => meta == null ? null : TryGetStringProperty(meta, "BuilderScript");

        /// <summary>도메인 메타에서 DumpScript 문자열 가져오기.</summary>
        private static string? GetDumpScript(object? meta)
            => meta == null ? null : TryGetStringProperty(meta, "DumpScript");

        /// <summary>도메인 메타에서 Label 문자열 가져오기.</summary>
        private static string? GetLabel(object? meta)
            => meta == null ? null : TryGetStringProperty(meta, "Label");

        /// <summary>도메인 메타에서 IsBuiltIn bool 가져오기.</summary>
        private static bool GetIsBuiltIn(object? meta)
        {
            if (meta == null) return false;
            try
            {
                var p = meta.GetType().GetProperty("IsBuiltIn");
                var v = p?.GetValue(meta);
                return v is bool b && b;
            }
            catch { return false; }
        }

        /// <summary>
        /// 빌드 스크립트 위치 결정.
        /// ★ v2.7.14: 사용자 도메인의 BuilderScript 가 없거나 파일이 없으면
        ///   빌트인 cs 폴더로 폴백하지 않고 즉시 null 반환 (빌드 거부 유도).
        ///   이전에는 LocateScript(unknownDomain) 이 build_kg_cs.py 로 폴백되어
        ///   사용자 도메인 빌드 스크립트가 사라져도 cs 빌더가 조용히 실행되는 버그가 있었음.
        /// </summary>
        private string? LocateBuildScriptForCurrent()
        {
            var meta = GetCurrentDomainMeta();
            if (meta != null)
            {
                // KgBuilderRunner.LocateScript(KgDomain) 오버로드 (v2.7.6 이상에서 추가).
                // reflection 으로 안전하게 호출 — 오버로드가 없는 환경에서도 컴파일 OK.
                var scriptByMeta = TryInvokeStaticMethod<string>(
                    typeof(KgBuilderRunner), "LocateScript", new[] { meta.GetType() }, new object[] { meta });
                if (scriptByMeta != null) return scriptByMeta;

                // 메타 기반 메서드가 없는 환경 — BuilderScript 절대경로를 직접 시도
                var builderScript = GetBuilderScript(meta);
                bool isCustom = !GetIsBuiltIn(meta);
                if (isCustom)
                {
                    // ★ 사용자 도메인 — BuilderScript 가 비어있거나 절대경로 파일이 없으면
                    //   빌트인 cs 폴백을 거부하고 null 반환.
                    //   사용자가 [경로변경] 처럼 빌더를 다시 지정하거나, 도메인을 다시 등록해야 함.
                    if (string.IsNullOrEmpty(builderScript)) return null;
                    if (Path.IsPathRooted(builderScript))
                    {
                        // 절대경로면 그 파일 존재만 확인
                        return File.Exists(builderScript) ? builderScript : null;
                    }
                    // 사용자 도메인인데 BuilderScript 가 절대경로가 아닌 경우 (드물게 파일명만)
                    // — kg_builder 폴더에서 그 이름으로 검색만 시도, 없으면 null
                    var inFolder = TryInvokeStaticMethod<string>(
                        typeof(KgBuilderRunner), "GetSearchedPaths",
                        new[] { typeof(string) }, new object[] { builderScript });
                    // GetSearchedPaths 는 List<string> 을 반환하므로 위 호출은 사실 사용 못 함 —
                    // 안전하게 그냥 null 반환 (빌트인 폴백 안 함)
                    return null;
                }
            }
            // 빌트인 도메인만 코드 기반 LocateScript 사용 (cs/photo/cmp/etch/thinfilm)
            return KgBuilderRunner.LocateScript(SelectedDomain);
        }

        /// <summary>
        /// 2단계 빌드의 dump 스크립트 위치.
        /// 빌트인 photo: KgBuilderRunner.LocateDumpScript
        /// 사용자 python_engine_folder: domainMeta.DumpScript 절대경로
        /// </summary>
        private string? LocateDumpScriptForCurrent()
        {
            if (SelectedDomain == KnowledgeGraphService.DOMAIN_PHOTO)
                return KgBuilderRunner.LocateDumpScript(SelectedDomain);

            var meta = GetCurrentDomainMeta();
            if (meta != null && GetBuilderKind(meta) == "python_engine_folder")
            {
                var ds = GetDumpScript(meta);
                if (!string.IsNullOrWhiteSpace(ds) && Path.IsPathRooted(ds) && File.Exists(ds))
                    return ds;
            }
            return null;
        }

        /// <summary>
        /// 2단계 빌드의 dump 산출물 (.cs 메타) 경로.
        /// ★ v2.7.22: SelectedDomain (도메인 코드) 기반으로 단순화 — RunAsync 산출 규칙과 일치.
        /// </summary>
        private string? LocateGeneratedCsForCurrent()
        {
            // v2.7.22: KgBuilderRunner.OutputGeneratedCsPath(string) 오버로드 우선 시도
            //          (도메인 코드 기반 — kg_{code}.meta.cs / 빌트인 photo 는 PhotoEngineMeta.cs)
            var path = TryInvokeStaticMethod<string>(
                typeof(KgBuilderRunner), "OutputGeneratedCsPath",
                new[] { typeof(string) }, new object[] { SelectedDomain });
            if (!string.IsNullOrEmpty(path)) return path;

            // 폴백 1: KgDomain 오버로드 (v2.7.8 사용자 환경 호환)
            var meta = GetCurrentDomainMeta();
            if (meta != null)
            {
                var p2 = TryInvokeStaticMethod<string>(
                    typeof(KgBuilderRunner), "OutputGeneratedCsPath",
                    new[] { meta.GetType() }, new object[] { meta });
                if (!string.IsNullOrEmpty(p2)) return p2;
            }

            // 폴백 2: 빌트인 photo (v2.4 옛 시그니처)
            return TryInvokeStaticMethod<string>(
                typeof(KgBuilderRunner), "LocateGeneratedCs",
                new[] { typeof(string) }, new object[] { SelectedDomain });
        }

        /// <summary>
        /// 정적 메서드를 reflection 으로 안전하게 호출. 메서드가 없으면 default(T) 반환.
        /// </summary>
        private static T? TryInvokeStaticMethod<T>(System.Type type, string name, System.Type[] argTypes, object[] args)
        {
            try
            {
                var mi = type.GetMethod(name,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, argTypes, null);
                if (mi == null) return default;
                var ret = mi.Invoke(null, args);
                return ret == null ? default : (T)ret;
            }
            catch { return default; }
        }

        /// <summary>리플렉션으로 안전하게 string 프로퍼티 가져오기.</summary>
        private static string? TryGetStringProperty(object obj, string propName)
        {
            try
            {
                var p = obj.GetType().GetProperty(propName);
                return p?.GetValue(obj) as string;
            }
            catch { return null; }
        }

        /// <summary>두 경로가 같은 파일/폴더를 가리키는지. Windows 대소문자 무시.</summary>
        private static bool PathEquals(string a, string b)
        {
            try
            {
                var fa = Path.GetFullPath(a).TrimEnd('\\', '/');
                var fb = Path.GetFullPath(b).TrimEnd('\\', '/');
                return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>빌드 스크립트를 못 찾았을 때 안내 메시지.</summary>
        private void ShowScriptNotFoundError()
        {
            var meta = GetCurrentDomainMeta();
            string scriptName = GetBuilderScript(meta) ?? "";
            bool isCustom = meta != null && !GetIsBuiltIn(meta);

            if (string.IsNullOrEmpty(scriptName))
            {
                scriptName = SelectedDomain switch
                {
                    KnowledgeGraphService.DOMAIN_PHOTO    => "build_kg_photo.py",
                    KnowledgeGraphService.DOMAIN_CMP      => "build_kg_cmp.py",
                    KnowledgeGraphService.DOMAIN_ETCH     => "build_kg_etch.py",
                    KnowledgeGraphService.DOMAIN_THINFILM => "build_kg_thinfilm.py",
                    _                                     => "build_kg_cs.py",
                };
            }

            // ★ v2.7.14: 사용자 도메인일 때는 더 정확한 안내 (등록한 절대경로가 사라졌다는 사실 명시)
            if (isCustom)
            {
                MessageBox.Show(
                    $"[{SelectedDomainLabel}] 도메인의 빌드 스크립트가 존재하지 않습니다.\n\n" +
                    $"등록된 경로:\n  {scriptName}\n\n" +
                    "원본 .py 파일이 이동/이름변경/삭제 되었습니다.\n" +
                    "이 상태에서는 다른 빌드 스크립트가 의도치 않게 사용될 위험이 있어\n" +
                    "빌드를 거부합니다.\n\n" +
                    "해결 방법:\n" +
                    "  • 원래 위치에 .py 파일을 다시 두기, 또는\n" +
                    "  • 도메인을 [– 도메인] 으로 삭제 후 [+ 도메인] 으로 다시 등록",
                    "빌드 스크립트 부재 — 빌드 거부",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 빌트인 도메인 — 기존 안내
            var searched = Path.IsPathRooted(scriptName)
                ? new System.Collections.Generic.List<string> { scriptName }
                : KgBuilderRunner.GetSearchedPaths(scriptName);
            var searchList = searched.Count > 0
                ? string.Join("\n  ", searched.ConvertAll(p => "• " + p))
                : "(없음)";

            string hint = KgBuilderRunner.IsDevEnvironment()
                ? $"[개발 환경]\n프로젝트 루트(.csproj 위치) 하위에 kg_builder 폴더가 있고\n그 안에 {Path.GetFileName(scriptName)} 가 존재하는지 확인하세요."
                : $"[설치본]\n설치 폴더의 kg_builder 폴더에\n{Path.GetFileName(scriptName)} 가 있는지 확인하세요.";

            MessageBox.Show(
                $"{scriptName} 를 찾을 수 없습니다.\n\n" +
                $"검색한 위치 (모두 부재):\n  {searchList}\n\n" +
                hint,
                "빌드 스크립트 없음",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(없음)";
            return s.Length <= max ? s : s.Substring(0, max) + "\n...(생략)";
        }
    }
}
