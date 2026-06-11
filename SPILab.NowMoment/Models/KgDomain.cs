using System;

namespace SPILab.NowMoment.Models
{
    /// <summary>
    /// KG 도메인 등록부 — kg_domain 테이블의 한 행에 대응.
    ///
    /// builder_kind:
    ///   "none"                 : 빌더 없음. TTL/JSON 직접 임포트로만 채움.
    ///   "cs_file"              : C# 엔진 파일 1개를 --src 로 받음 (build_kg_*.py).
    ///   "python_engine_folder" : Python engine 폴더를 --src 로 받음.
    ///                            v2.7.8: 2단계 빌드 — DumpScript 가 .cs 메타로 변환,
    ///                            BuilderScript 가 그 .cs 를 분석.
    ///
    /// (구버전 코드와의 호환을 위해 DB 의 builder_kind 값이 "python_folder" 로 저장된 경우도
    ///  KgDomainService 가 자동으로 "python_engine_folder" 로 정규화한다.)
    /// </summary>
    public class KgDomain
    {
        public string Code           { get; set; } = "";
        public string Label          { get; set; } = "";
        public string BuilderKind    { get; set; } = "none";
        public string BuilderScript  { get; set; } = "";   // build 단계 .py 절대경로 (또는 빌트인은 파일명)
        public string DumpScript     { get; set; } = "";   // ★ v2.7.8: dump 단계 .py 절대경로 (python_engine_folder 일 때만)
        public string OutputBasename { get; set; } = "";
        public bool   IsBuiltIn      { get; set; }
        public DateTime CreatedAt    { get; set; } = DateTime.Now;

        public override string ToString() => $"{Label} ({Code})";
    }
}
