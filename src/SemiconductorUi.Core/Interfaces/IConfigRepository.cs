using SemiconductorUi.Models;

namespace SemiconductorUi
{
    /// <summary>
    /// 설정 데이터 저장소 인터페이스
    /// Phase 4.1: Repository 패턴 개선
    /// </summary>
    public interface IConfigRepository
    {
        /// <summary>
        /// 설정 로드
        /// </summary>
        EnvThresholdSnapshot Load();

        /// <summary>
        /// 설정 저장
        /// </summary>
        void Save(EnvThresholdSnapshot config);

        /// <summary>
        /// 기본 설정 생성 (파일이 없을 때)
        /// </summary>
        EnvThresholdSnapshot CreateDefault();
    }
}

