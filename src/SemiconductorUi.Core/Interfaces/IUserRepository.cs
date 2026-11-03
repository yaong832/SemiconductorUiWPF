using System.Collections.Generic;
using SemiconductorUi.Models;

namespace SemiconductorUi
{
    /// <summary>
    /// 사용자 데이터 저장소 인터페이스
    /// Phase 4.1: Repository 패턴 개선
    /// </summary>
    public interface IUserRepository
    {
        /// <summary>
        /// 모든 사용자 로드
        /// </summary>
        List<User> LoadAll();

        /// <summary>
        /// 모든 사용자 저장
        /// </summary>
        void SaveAll(List<User> users);

        /// <summary>
        /// 사용자명으로 사용자 찾기
        /// </summary>
        User FindByUsername(string username);

        /// <summary>
        /// 사용자 인증
        /// </summary>
        bool ValidateUser(string username, string password);

        /// <summary>
        /// 기본 사용자 데이터 생성 (파일이 없을 때)
        /// </summary>
        List<User> EnsureSeedDefaults();
    }
}

