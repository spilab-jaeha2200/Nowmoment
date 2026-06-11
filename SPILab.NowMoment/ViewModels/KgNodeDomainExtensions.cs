// ════════════════════════════════════════════════════════════
// KgNodeDomainExtensions.cs   (v4 Phase 6)
//
// KgNode.Id 의 prefix("cs:Loader" → "cs")를 도메인 표시용 문자열로
// 추출하기 위한 확장 메서드.  편집 다이얼로그 §4.3 의 KG 노드 표
// "도메인" 컬럼에서만 사용한다.
//
// 별도 파일로 분리한 이유: Models/KgModels.cs 는 KG 빌더가 생성하는
// 데이터의 직접 매핑이므로, 표시 전용 가공은 ViewModels 측에 둔다.
// ════════════════════════════════════════════════════════════
using SPILab.NowMoment.Models;

namespace SPILab.NowMoment.ViewModels
{
    internal static class KgNodeDomainExtensions
    {
        /// <summary>NodeId 의 ':' 앞 prefix 를 도메인 문자열로 반환. 없으면 ""</summary>
        public static string ResolveDomain(this KgNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Id)) return "";
            var i = node.Id.IndexOf(':');
            return i > 0 ? node.Id.Substring(0, i) : "";
        }

        /// <summary>NodeId 문자열에서 도메인 prefix 만 추출.</summary>
        public static string DomainOf(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return "";
            var i = nodeId.IndexOf(':');
            return i > 0 ? nodeId.Substring(0, i) : "";
        }
    }
}
