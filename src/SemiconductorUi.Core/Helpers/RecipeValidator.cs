using System;

namespace SemiconductorUi.Helpers
{
    /// <summary>
    /// 레시피 파라미터 검증을 담당하는 클래스
    /// </summary>
    public static class RecipeValidator
    {
        /// <summary>
        /// 공정 시간 최소값 (초)
        /// </summary>
        public const int MinChamberDuration = 1;

        /// <summary>
        /// 웨이퍼 수를 검증하고 조정된 값을 반환
        /// </summary>
        /// <param name="waferCount">입력된 웨이퍼 수</param>
        /// <returns>검증 및 조정된 웨이퍼 수 (1 ~ MaxFoupCapacity)</returns>
        public static int ValidateWaferCount(int waferCount)
        {
            return Math.Max(1, Math.Min(AppSettings.MaxFoupCapacity, waferCount));
        }

        /// <summary>
        /// 공정 시간을 검증하고 조정된 값을 반환
        /// </summary>
        /// <param name="duration">입력된 공정 시간 (초)</param>
        /// <returns>검증 및 조정된 공정 시간 (최소 MinChamberDuration)</returns>
        public static int ValidateChamberDuration(int duration)
        {
            return Math.Max(MinChamberDuration, duration);
        }

        /// <summary>
        /// 모든 챔버 공정 시간을 검증하고 조정된 값을 반환
        /// </summary>
        /// <param name="chamberADuration">Chamber A 공정 시간</param>
        /// <param name="chamberBDuration">Chamber B 공정 시간</param>
        /// <param name="chamberCDuration">Chamber C 공정 시간</param>
        /// <returns>검증 및 조정된 공정 시간 (durA, durB, durC)</returns>
        public static (int durA, int durB, int durC) ValidateChamberDurations(int chamberADuration, int chamberBDuration, int chamberCDuration)
        {
            int durA = ValidateChamberDuration(chamberADuration);
            int durB = ValidateChamberDuration(chamberBDuration);
            int durC = ValidateChamberDuration(chamberCDuration);
            return (durA, durB, durC);
        }

        /// <summary>
        /// 공정 시간이 최소값 미만인지 확인
        /// </summary>
        /// <param name="chamberADuration">Chamber A 공정 시간</param>
        /// <param name="chamberBDuration">Chamber B 공정 시간</param>
        /// <param name="chamberCDuration">Chamber C 공정 시간</param>
        /// <returns>최소값 미만인 챔버가 있으면 true</returns>
        public static bool HasDurationBelowMinimum(int chamberADuration, int chamberBDuration, int chamberCDuration)
        {
            return chamberADuration < MinChamberDuration 
                   || chamberBDuration < MinChamberDuration 
                   || chamberCDuration < MinChamberDuration;
        }

        /// <summary>
        /// 레시피 파라미터 전체 검증
        /// </summary>
        /// <param name="waferCount">웨이퍼 수</param>
        /// <param name="chamberADuration">Chamber A 공정 시간</param>
        /// <param name="chamberBDuration">Chamber B 공정 시간</param>
        /// <param name="chamberCDuration">Chamber C 공정 시간</param>
        /// <returns>검증 결과 및 조정된 값</returns>
        public static RecipeValidationResult ValidateRecipeParameters(
            int waferCount, 
            int chamberADuration, 
            int chamberBDuration, 
            int chamberCDuration)
        {
            int validatedWaferCount = ValidateWaferCount(waferCount);
            var (durA, durB, durC) = ValidateChamberDurations(chamberADuration, chamberBDuration, chamberCDuration);
            bool hasWarning = HasDurationBelowMinimum(chamberADuration, chamberBDuration, chamberCDuration);

            return new RecipeValidationResult
            {
                WaferCount = validatedWaferCount,
                ChamberADuration = durA,
                ChamberBDuration = durB,
                ChamberCDuration = durC,
                HasWarning = hasWarning
            };
        }
    }

    /// <summary>
    /// 레시피 검증 결과
    /// </summary>
    public class RecipeValidationResult
    {
        public int WaferCount { get; set; }
        public int ChamberADuration { get; set; }
        public int ChamberBDuration { get; set; }
        public int ChamberCDuration { get; set; }
        public bool HasWarning { get; set; }
    }
}

