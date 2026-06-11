// ════════════════════════════════════════════════════════════════════
// CoreContractDtos.cs — NowMoment v4.1 (Core 분리 / Phase 1)
//
// IKgBuilder 계약에서 주고받는 순수 데이터 객체.
// 기존 Services/KgBuilderRunner.cs 의 BuildResult 와 호환되도록 필드를
// 동일하게 유지하여, 호출 측 코드 변경을 최소화한다.
// ════════════════════════════════════════════════════════════════════
using System;

namespace SPILab.NowMoment.Core.Contracts
{
    /// <summary>KG 빌드 요청 — 도메인·입력경로·Python 경로 등 빌드에 필요한 입력.</summary>
    public sealed class BuildRequest
    {
        /// <summary>도메인 코드 (cs / photo / cmp / etch / thinfilm / 사용자 도메인).</summary>
        public string Domain { get; init; } = "cs";

        /// <summary>물리 엔진 .cs 파일 경로 또는 python_engine 폴더 경로 (--src).</summary>
        public string SourcePath { get; init; } = "";

        /// <summary>빌더 스크립트 경로. null 이면 구현체가 도메인으로 자동 탐색.</summary>
        public string? ScriptPath { get; init; }

        /// <summary>2단계 빌드용 dump 스크립트 경로 (photo 등). 1단계면 null.</summary>
        public string? DumpScriptPath { get; init; }

        /// <summary>사용할 Python 실행파일 절대경로. 호출자가 사전 확보·검증.</summary>
        public string? PythonExe { get; init; }
    }

    /// <summary>KG 빌드 결과 — 기존 KgBuilderRunner.BuildResult 와 필드 호환.</summary>
    public sealed class BuildResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public string JsonPath { get; set; } = "";
        public string TtlPath { get; set; } = "";
        public TimeSpan Elapsed { get; set; }
        public string? PythonUsed { get; set; }
        public string? ScriptPath { get; set; }

        /// <summary>v4.1: 산출 KG 의 룰 수 (회귀 검증·감사 로그용). 미집계 시 -1.</summary>
        public int RuleCount { get; set; } = -1;

        /// <summary>v4.1: 이 빌드를 수행한 Secure-Verify 세션 ID (워터마킹·감사용).</summary>
        public string CoreSession { get; set; } = "";

        /// <summary>Core 미가용 등으로 빌드 자체가 거부된 경우 true.</summary>
        public bool Skipped { get; set; }

        public static BuildResult Fail(string reason) =>
            new() { Success = false, StdErr = reason };

        public static BuildResult SkippedCore(string reason) =>
            new() { Success = false, Skipped = true, StdErr = reason };
    }
}
