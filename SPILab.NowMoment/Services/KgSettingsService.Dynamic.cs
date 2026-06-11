// ════════════════════════════════════════════════════════════════════
// KgSettingsService.Dynamic.cs (v2.7.5)
//
// 변경:
//   * 도메인별 "마지막으로 사용한 임포트 파일 경로" 키 헬퍼 추가
//     - 키 패턴: "last_import_path_{domain}"
//     - 도메인 등록 시 사용자가 고른 TTL/JSON 경로를 여기 저장
//     - 이후 [KG 임포트] 버튼이 builder_kind='none' 도메인에 대해 이 값을 우선 읽음
//
// 사용 조건:
//   * KgSettingsService.cs 의 클래스 선언이 partial 이어야 함 (이전 단계에서 적용됨)
// ════════════════════════════════════════════════════════════════════
namespace SPILab.NowMoment.Services
{
    public partial class KgSettingsService
    {
        /// <summary>
        /// 동적 도메인용 src 경로 키 빌더. 빌트인 5종은 기존 KEY_BUILDER_SRC_XX 와 동일.
        /// </summary>
        public static string KeyForDomainDynamic(string code) => code switch
        {
            "cs"       => KEY_BUILDER_SRC_CS,
            "photo"    => KEY_BUILDER_SRC_PHOTO,
            "cmp"      => KEY_BUILDER_SRC_CMP,
            "etch"     => KEY_BUILDER_SRC_ETCH,
            "thinfilm" => KEY_BUILDER_SRC_THINFILM,
            _          => $"builder_src_path_{code}",
        };

        /// <summary>
        /// ★ v2.7.5: 도메인의 마지막 임포트 파일 경로 키.
        /// builder_kind='none' 도메인이 [KG 임포트] 시 이 값을 우선 사용한다.
        /// </summary>
        public static string KeyForLastImport(string code) => $"last_import_path_{code}";
    }
}
