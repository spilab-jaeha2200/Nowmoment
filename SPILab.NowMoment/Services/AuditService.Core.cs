// ════════════════════════════════════════════════════════════════════
// AuditService.Core.cs — NowMoment v4.1 Phase 3 (작업 4)
//
// 개선 개발계획서 5.3 "감사 로그 확장" 구현.
//
// v4.0 의 audit_log 는 자산 CRUD diff 만 기록했다. v4.1 은 Core 접근
// 이벤트(인증·복호화·빌드·거부)를 같은 테이블에 적재한다 — 기존
// audit_log 인프라를 재사용하므로 신규 테이블이 필요 없다.
//
// 이벤트 타입 (audit_log.action):
//   core.verify  인증 성공         core.denied  인가 거부
//   core.unlock  번들 복호화 성공   core.build   KG 빌더 실행
//   core.lock    세션 종료         core.absent  Core 페이로드 없음
//
// ★ 작업 4 갱신: audit_log 에 actor_role / core_session 전용 컬럼이
//   추가되었다(계획서 5.3). LogCore 는 role·session 을 payload JSON 이
//   아닌 전용 컬럼에 기록한다 — 조회·필터·집계가 쉬워진다.
//   detail 만 payload JSON 으로 남긴다.
// ════════════════════════════════════════════════════════════════════
using System;

namespace SPILab.NowMoment.Services
{
    public partial class AuditService
    {
        /// <summary>
        /// Core 접근 이벤트 1건을 audit_log 에 적재한다.
        /// SecureVerifyGate / CoreProviderLoader 의 auditSink 콜백이 이 메서드를
        /// 가리키도록 App 시작 시 배선한다.
        /// </summary>
        /// <param name="action">core.verify / core.build / core.denied 등.</param>
        /// <param name="actorRole">CoreOwner / CoreDeveloper / CoreRunner / ShellOnly.</param>
        /// <param name="detail">자유 형식 상세 (session=..., domain=..., reason=... 등).</param>
        public void LogCore(string action, string actorRole, string detail)
        {
            // detail 문자열에서 'session=...' 토큰이 있으면 core_session 컬럼으로 분리.
            // (SecureVerifyGate 의 기존 detail 포맷 "session=xxx actor=yyy" 와 호환)
            string session = ExtractToken(detail, "session=");

            // audit_log 전용 컬럼(actor_role, core_session)에 직접 기록한다.
            // assetType 슬롯에 'core' 를 넣어 자산 이벤트와 구분, assetId 는 없음(null).
            string payload = Serialize(new
            {
                detail = detail ?? "",
                at     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });

            _db.WriteAudit(
                action:      string.IsNullOrWhiteSpace(action) ? "core.event" : action,
                assetType:   "core",
                assetId:     null,
                diffJson:    payload,
                actor:       "local",
                actorRole:   actorRole ?? "",
                coreSession: session);
        }

        /// <summary>
        /// CoreProviderLoader / SecureVerifyGate 에 넘길 감사 콜백을 생성한다.
        /// 시그니처: (action, actorRole, detail) → void.
        /// </summary>
        public Action<string, string, string> CreateCoreAuditSink()
            => (action, role, detail) =>
            {
                try { LogCore(action, role, detail); }
                catch { /* 감사 적재 실패가 Core 동작을 막아서는 안 됨 */ }
            };

        /// <summary>
        /// "key=value ..." 형식 문자열에서 지정 prefix 의 값을 추출한다.
        /// 예: ExtractToken("session=ab12 actor=홍길동", "session=") → "ab12"
        /// </summary>
        private static string ExtractToken(string? text, string prefix)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int idx = text.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) return "";
            int start = idx + prefix.Length;
            int end = text.IndexOf(' ', start);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }
    }
}
