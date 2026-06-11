using System.IO;
using System.Windows;
using Microsoft.Win32;
using SPILab.NowMoment.Models;
using SPILab.NowMoment.Services;

namespace SPILab.NowMoment.Views
{
    /// <summary>
    /// 새 KG 도메인 등록 다이얼로그 (v2.7.10).
    ///
    /// v2.7.9 → v2.7.10 변경:
    ///   * 임포트 파일 비활성 시 라벨 텍스트에서 "(해당 없음)" 제거.
    ///     행이 회색으로 비활성 처리되므로 보조 텍스트 없이 깔끔히 "임포트 파일" 만 표시.
    ///
    /// v2.7.8 → v2.7.9 변경:
    ///   * 임포트 파일 행이 'none' 일 때만 활성. cs_file / python_engine_folder 일 때는 비활성.
    ///   * cs_file / python_engine_folder 도메인은 등록 후 [KG 빌드] 로 데이터를 채우는 게 권장 흐름.
    ///     (등록 직후 자동 임포트는 'none' 도메인에서만 발생)
    /// </summary>
    public partial class KgDomainEditDialog : Window
    {
        public KgDomain Result { get; private set; } = new KgDomain();
        public string ImportPath    { get; private set; } = "";
        public string EngineSrcPath { get; private set; } = "";

        public KgDomainEditDialog()
        {
            InitializeComponent();
            UpdateRowsForKind();
        }

        private void Kind_Changed(object sender, RoutedEventArgs e)
        {
            if (LblBuildScript == null) return;
            UpdateRowsForKind();
        }

        private string CurrentKind()
        {
            if (RbCs.IsChecked == true)    return "cs_file";
            if (RbPyDir.IsChecked == true) return "python_engine_folder";
            return "none";
        }

        private void UpdateRowsForKind()
        {
            string kind = CurrentKind();
            bool needsBuilder = kind == "cs_file" || kind == "python_engine_folder";
            bool isPyDir      = kind == "python_engine_folder";
            bool isNone       = kind == "none";

            // 빌드 스크립트 / 엔진 — cs_file + python_engine_folder
            LblBuildScript.IsEnabled     = needsBuilder;
            TxtBuildScriptPath.IsEnabled = needsBuilder;
            BtnBrowseScript.IsEnabled    = needsBuilder;
            LblEngineFile.IsEnabled      = needsBuilder;
            TxtEnginePath.IsEnabled      = needsBuilder;
            BtnBrowseEngine.IsEnabled    = needsBuilder;

            // Dump 스크립트 — python_engine_folder 만
            LblDumpScript.IsEnabled      = isPyDir;
            TxtDumpScriptPath.IsEnabled  = isPyDir;
            BtnBrowseDump.IsEnabled      = isPyDir;

            // ★ v2.7.9: 임포트 파일 — 'none' 일 때만 활성
            LblImportFile.IsEnabled      = isNone;
            TxtImportPath.IsEnabled      = isNone;
            BtnBrowseImport.IsEnabled    = isNone;

            LblEngineFile.Text = kind switch
            {
                "python_engine_folder" => "Python engine 폴더 *",
                "cs_file"              => "엔진 파일 (.cs) *",
                _                      => "엔진 파일 / 폴더",
            };

            // 임포트 파일 라벨 — 활성일 때만 별표(*) 표시. 비활성 시에는 "임포트 파일" 만.
            //   ★ v2.7.10: "해당 없음" 보조 텍스트 제거 (행 전체가 회색이라 시각적으로 충분히 구분됨)
            LblImportFile.Text = isNone ? "임포트 파일 *" : "임포트 파일";

            TxtKindHelp.Text = kind switch
            {
                "cs_file" =>
                    "빌더 종류 = 'C# 엔진 파일' — 1단계 빌드. " +
                    "build_kg_*.py 가 .cs 파일을 정적분석해 KG 의 .json/.ttl 을 생성. " +
                    "등록 후 [KG 빌드] 를 눌러 데이터를 만드세요. " +
                    "출력은 %APPDATA%\\Roaming\\SPILab\\NowMoment\\kg_builder\\ 에 저장됩니다.",
                "python_engine_folder" =>
                    "빌더 종류 = 'Python engine 폴더' — 2단계 빌드. " +
                    "1) Dump 스크립트가 폴더 → C# 메타 변환  " +
                    "2) 빌드 스크립트가 그 메타 → KG .json/.ttl 변환. " +
                    "등록 후 [KG 빌드] 를 눌러 데이터를 만드세요. " +
                    "모든 산출물은 %APPDATA%\\Roaming\\SPILab\\NowMoment\\kg_builder\\ 에 저장됩니다.",
                _ =>
                    "빌더 종류 = '없음' — TTL/JSON 파일을 [임포트 파일] 에서 선택하세요. " +
                    "등록 직후 자동 임포트됩니다.",
            };
        }

        // ── [찾기] 버튼들 ────────────────────────────────
        private void BtnBrowseScript_Click(object sender, RoutedEventArgs e)
            => BrowsePyScript(TxtBuildScriptPath, "빌드 스크립트 선택 — build_kg_*.py");

        private void BtnBrowseDump_Click(object sender, RoutedEventArgs e)
            => BrowsePyScript(TxtDumpScriptPath, "Dump 스크립트 선택 — dump_*.py");

        private void BrowsePyScript(System.Windows.Controls.TextBox target, string title)
        {
            var dlg = new OpenFileDialog
            {
                Title  = title,
                Filter = "Python 스크립트 (*.py)|*.py|모든 파일|*.*",
            };
            SetInitialDirIfExists(dlg, target.Text);
            if (dlg.ShowDialog() == true) target.Text = dlg.FileName;
        }

        private void BtnBrowseEngine_Click(object sender, RoutedEventArgs e)
        {
            string kind = CurrentKind();

            if (kind == "python_engine_folder")
            {
                try
                {
                    var fdlg = new OpenFolderDialog { Title = "Python engine 폴더 선택" };
                    if (!string.IsNullOrEmpty(TxtEnginePath.Text) && Directory.Exists(TxtEnginePath.Text))
                        fdlg.InitialDirectory = TxtEnginePath.Text;
                    if (fdlg.ShowDialog() == true) TxtEnginePath.Text = fdlg.FolderName;
                    return;
                }
                catch (System.TypeLoadException)
                {
                    var dlg = new OpenFileDialog
                    {
                        Title  = "Python engine 폴더 안의 임의 .py 파일 선택",
                        Filter = "Python 파일 (*.py)|*.py|모든 파일 (*.*)|*.*",
                    };
                    if (!string.IsNullOrEmpty(TxtEnginePath.Text) && Directory.Exists(TxtEnginePath.Text))
                        dlg.InitialDirectory = TxtEnginePath.Text;
                    if (dlg.ShowDialog() == true)
                    {
                        var dir = Path.GetDirectoryName(dlg.FileName);
                        if (!string.IsNullOrEmpty(dir)) TxtEnginePath.Text = dir;
                    }
                    return;
                }
            }

            var fileDlg = new OpenFileDialog
            {
                Title  = "C# 엔진 파일 선택 — *PhysicsEngine.cs",
                Filter = "C# 소스 (*.cs)|*.cs|모든 파일|*.*",
            };
            SetInitialDirIfExists(fileDlg, TxtEnginePath.Text);
            if (fileDlg.ShowDialog() == true) TxtEnginePath.Text = fileDlg.FileName;
        }

        private void BtnBrowseImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "임포트할 KG 파일 선택 — TTL 또는 JSON",
                Filter = "KG 파일 (*.ttl;*.json;*.jsonld;*.rdf;*.nt)|*.ttl;*.json;*.jsonld;*.rdf;*.nt|" +
                         "Turtle (*.ttl)|*.ttl|" +
                         "JSON-LD (*.json;*.jsonld)|*.json;*.jsonld|" +
                         "모든 파일|*.*",
            };
            if (!SetInitialDirIfExists(dlg, TxtImportPath.Text))
            {
                try
                {
                    var bd = KgBuilderRunner.OutputDir;
                    if (Directory.Exists(bd)) dlg.InitialDirectory = bd;
                }
                catch { }
            }
            if (dlg.ShowDialog() == true) TxtImportPath.Text = dlg.FileName;
        }

        private static bool SetInitialDirIfExists(OpenFileDialog dlg, string? currentText)
        {
            if (string.IsNullOrWhiteSpace(currentText)) return false;
            string? dir = Directory.Exists(currentText) ? currentText : Path.GetDirectoryName(currentText);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                dlg.InitialDirectory = dir;
                return true;
            }
            return false;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var code = TxtCode.Text?.Trim() ?? "";
                KgDomainService.ValidateCode(code);
                var label = TxtLabel.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(label))
                {
                    TxtError.Text = "표시 라벨을 입력하세요.";
                    return;
                }

                string kind        = CurrentKind();
                string buildScript = TxtBuildScriptPath.Text?.Trim() ?? "";
                string dumpScript  = TxtDumpScriptPath.Text?.Trim() ?? "";
                string enginePath  = TxtEnginePath.Text?.Trim() ?? "";
                string importPath  = TxtImportPath.Text?.Trim() ?? "";

                // ── 빌더 검증 (cs_file / python_engine_folder) ──
                if (kind == "cs_file" || kind == "python_engine_folder")
                {
                    if (string.IsNullOrEmpty(buildScript))
                    { TxtError.Text = "빌드 스크립트(.py) 경로를 선택하세요."; return; }
                    if (!File.Exists(buildScript))
                    { TxtError.Text = $"빌드 스크립트를 찾을 수 없습니다:\n{buildScript}"; return; }

                    if (string.IsNullOrEmpty(enginePath))
                    {
                        TxtError.Text = kind == "python_engine_folder"
                            ? "Python engine 폴더 경로를 선택하세요."
                            : "C# 엔진 파일(.cs) 경로를 선택하세요.";
                        return;
                    }
                    bool engineOk = kind == "python_engine_folder"
                        ? Directory.Exists(enginePath)
                        : File.Exists(enginePath);
                    if (!engineOk)
                    {
                        TxtError.Text = kind == "python_engine_folder"
                            ? $"Python engine 폴더를 찾을 수 없습니다:\n{enginePath}"
                            : $"엔진 파일을 찾을 수 없습니다:\n{enginePath}";
                        return;
                    }

                    if (kind == "python_engine_folder")
                    {
                        if (string.IsNullOrEmpty(dumpScript))
                        { TxtError.Text = "Dump 스크립트(.py) 경로를 선택하세요."; return; }
                        if (!File.Exists(dumpScript))
                        { TxtError.Text = $"Dump 스크립트를 찾을 수 없습니다:\n{dumpScript}"; return; }
                    }

                    // ★ v2.7.9: cs_file / python_engine_folder 는 임포트 파일 검증 안 함 (행 자체가 비활성)
                    importPath = "";
                }
                else
                {
                    // 'none' 도메인 — 임포트 파일 필수
                    if (string.IsNullOrEmpty(importPath))
                    { TxtError.Text = "임포트 파일(.ttl / .json) 경로를 선택하세요."; return; }
                    if (!File.Exists(importPath))
                    { TxtError.Text = $"임포트 파일을 찾을 수 없습니다:\n{importPath}"; return; }
                }

                Result = new KgDomain
                {
                    Code           = code,
                    Label          = label,
                    BuilderKind    = kind,
                    BuilderScript  = (kind == "cs_file" || kind == "python_engine_folder") ? buildScript : "",
                    DumpScript     = (kind == "python_engine_folder") ? dumpScript : "",
                    OutputBasename = TxtBasename.Text?.Trim() ?? "",
                };
                EngineSrcPath = enginePath;
                ImportPath    = importPath;
                DialogResult  = true;
                Close();
            }
            catch (System.Exception ex)
            {
                TxtError.Text = ex.Message;
            }
        }
    }
}
