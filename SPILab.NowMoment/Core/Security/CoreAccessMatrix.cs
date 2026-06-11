// ════════════════════════════════════════════════════════════════════
// CoreAccessMatrix.cs — NowMoment v4.1 Phase 3 (작업 3)
//
// 개선 개발계획서 5.2 "권한 등급":
//   Core-Owner / Core-Developer / Core-Runner / Shell-Only 4개 역할의
//   권한 매트릭스를 관리한다.
//
//   매트릭스는 JSON 파일(core_access.json)로 보관한다. 계획서 8장
//   권고대로 권한 부여는 조직이 결정하므로, 코드가 아닌 외부 설정
//   파일로 분리하여 조직이 운영 중 갱신할 수 있게 한다.
//
//   비밀번호는 평문 저장하지 않는다 — PBKDF2-SHA256 해시 + salt 만
//   저장하며, 본 클래스가 검증한다. (SSO 사용 시에는 SSO 가 인증을
//   대신하므로 해시가 비어 있어도 된다.)
//
// core_access.json 예시:
// {
//   "schema": "1.0",
//   "entries": [
//     {
//       "employeeId": "SPL-001",
//       "displayName": "홍길동",
//       "role": "CoreOwner",
//       "pbkdf2": { "salt": "<base64>", "hash": "<base64>", "iterations": 200000 }
//     },
//     { "employeeId": "SPL-042", "displayName": "김개발", "role": "CoreRunner",
//       "pbkdf2": { ... } }
//   ]
// }
// ════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SPILab.NowMoment.Core.Security
{
    /// <summary>PBKDF2-SHA256 해시 파라미터.</summary>
    public sealed class Pbkdf2Hash
    {
        [JsonPropertyName("salt")]       public string Salt { get; set; } = "";
        [JsonPropertyName("hash")]       public string Hash { get; set; } = "";
        [JsonPropertyName("iterations")] public int Iterations { get; set; } = 200_000;
    }

    /// <summary>권한 매트릭스의 사용자 1명 항목.</summary>
    public sealed class CoreAccessEntry
    {
        [JsonPropertyName("employeeId")]  public string EmployeeId { get; set; } = "";
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("role")]        public string Role { get; set; } = "ShellOnly";
        [JsonPropertyName("pbkdf2")]      public Pbkdf2Hash? Pbkdf2 { get; set; }
    }

    /// <summary>core_access.json 의 루트.</summary>
    public sealed class CoreAccessDocument
    {
        [JsonPropertyName("schema")]  public string Schema { get; set; } = "1.0";
        [JsonPropertyName("entries")] public List<CoreAccessEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// 권한 매트릭스. 사번 → 역할 조회와 로컬 비밀번호 검증을 담당한다.
    /// </summary>
    public sealed class CoreAccessMatrix
    {
        private readonly Dictionary<string, CoreAccessEntry> _byId;

        private CoreAccessMatrix(IEnumerable<CoreAccessEntry> entries)
        {
            _byId = entries.ToDictionary(
                e => e.EmployeeId, e => e, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>비어 있는 매트릭스 (파일 부재 시 — 모두 Shell-Only).</summary>
        public static CoreAccessMatrix Empty() =>
            new(Array.Empty<CoreAccessEntry>());

        /// <summary>core_access.json 을 로드한다. 파일이 없으면 빈 매트릭스.</summary>
        public static CoreAccessMatrix Load(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return Empty();

            try
            {
                var doc = JsonSerializer.Deserialize<CoreAccessDocument>(
                    File.ReadAllText(jsonPath));
                return new CoreAccessMatrix(doc?.Entries ?? new List<CoreAccessEntry>());
            }
            catch (JsonException)
            {
                // 손상된 매트릭스는 "권한 없음" 으로 안전하게 폴백
                return Empty();
            }
        }

        /// <summary>사번이 매트릭스에 등록되어 있는지.</summary>
        public bool Contains(string employeeId) =>
            !string.IsNullOrEmpty(employeeId) && _byId.ContainsKey(employeeId);

        /// <summary>사번의 역할을 조회. 미등록 시 ShellOnly.</summary>
        public CoreRole RoleOf(string employeeId)
        {
            if (employeeId != null && _byId.TryGetValue(employeeId, out var e)
                && Enum.TryParse<CoreRole>(e.Role, ignoreCase: true, out var role))
                return role;
            return CoreRole.ShellOnly;
        }

        /// <summary>사번의 표시 이름. 미등록 시 사번 그대로.</summary>
        public string DisplayNameOf(string employeeId) =>
            (employeeId != null && _byId.TryGetValue(employeeId, out var e)
                && !string.IsNullOrEmpty(e.DisplayName))
                ? e.DisplayName
                : employeeId ?? "";

        /// <summary>
        /// 로컬 비밀번호를 PBKDF2 해시와 대조한다.
        /// 해시 정보가 없으면(SSO 전용 계정 등) false 를 반환한다 —
        /// 로컬 인증 경로로는 통과시키지 않는다.
        /// </summary>
        public bool VerifyPassword(string employeeId, string password)
        {
            if (string.IsNullOrEmpty(password)) return false;
            if (employeeId == null || !_byId.TryGetValue(employeeId, out var e))
                return false;
            if (e.Pbkdf2 == null
                || string.IsNullOrEmpty(e.Pbkdf2.Salt)
                || string.IsNullOrEmpty(e.Pbkdf2.Hash))
                return false;

            byte[] salt, expected;
            try
            {
                salt     = Convert.FromBase64String(e.Pbkdf2.Salt);
                expected = Convert.FromBase64String(e.Pbkdf2.Hash);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: e.Pbkdf2.Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: expected.Length);

            // 타이밍 공격 방지 — 고정시간 비교
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        /// <summary>
        /// 비밀번호 해시를 생성한다 (운영 도구 — core_access.json 편집용).
        /// 새 계정 등록·비밀번호 변경 시 이 메서드로 Pbkdf2Hash 를 만든다.
        /// </summary>
        public static Pbkdf2Hash CreateHash(string password, int iterations = 200_000)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return new Pbkdf2Hash
            {
                Salt       = Convert.ToBase64String(salt),
                Hash       = Convert.ToBase64String(hash),
                Iterations = iterations,
            };
        }
    }
}
