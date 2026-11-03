using System.Collections.Generic;
using SemiconductorUi.Models;

namespace SemiconductorUi
{
    /// <summary>
    /// 레시피 데이터 저장소 인터페이스
    /// Phase 4.1: Repository 패턴 개선
    /// </summary>
    public interface IRecipeRepository
    {
        /// <summary>
        /// 모든 레시피 로드
        /// </summary>
        List<RecipeSnapshot> LoadAll();

        /// <summary>
        /// 모든 레시피 저장
        /// </summary>
        void SaveAll(List<RecipeSnapshot> recipes);

        /// <summary>
        /// 기본 레시피 데이터 생성 (파일이 없을 때)
        /// </summary>
        List<RecipeSnapshot> EnsureSeedDefaults();
    }
}

