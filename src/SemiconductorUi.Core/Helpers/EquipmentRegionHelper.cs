using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Controls;
using SemiconductorUi.Controllers;

namespace SemiconductorUi.Helpers
{
    /// <summary>
    /// EquipmentRegion 관련 유틸리티 메서드를 제공하는 Helper 클래스
    /// </summary>
    public static class EquipmentRegionHelper
    {
        /// <summary>
        /// 서보 위치를 Region으로 변환 (더 정확한 판별)
        /// </summary>
        /// <param name="x">Axis2 위치 (좌우)</param>
        /// <param name="y">Axis1 위치 (상하)</param>
        /// <param name="positions">TM 위치 좌표 세트</param>
        /// <returns>가장 가까운 EquipmentRegion</returns>
        public static EquipmentRegion DetermineRegionFromPosition(long x, long y, TmHardwareController.TmPositionSet positions)
        {
            if (positions == null)
            {
                return EquipmentRegion.TM;
            }

            const long tolerance = 50000; // 위치 허용 오차

            // 가장 가까운 위치 찾기 (거리 기반)
            var distances = new Dictionary<EquipmentRegion, long>
            {
                { EquipmentRegion.FoupA, Math.Abs(x - positions.FoupA_X) },
                { EquipmentRegion.FoupB, Math.Abs(x - positions.FoupB_X) },
                { EquipmentRegion.ChamberA, Math.Abs(x - positions.ChamberA_X) },
                { EquipmentRegion.ChamberB, Math.Abs(x - positions.ChamberB_X) },
                { EquipmentRegion.ChamberC, Math.Abs(x - positions.ChamberC_X) }
            };

            // 가장 가까운 Region 찾기
            var closestRegion = distances.OrderBy(d => d.Value).First();

            // 허용 오차 내에 있으면 해당 Region 반환
            if (closestRegion.Value < tolerance)
            {
                return closestRegion.Key;
            }

            // 홈 위치 또는 알 수 없는 위치
            return EquipmentRegion.TM;
        }

        /// <summary>
        /// Region에 따른 블레이드 각도 계산 (Canvas 그리기용)
        /// </summary>
        /// <param name="region">EquipmentRegion</param>
        /// <returns>라디안 각도</returns>
        public static float GetBladeAngleForCanvas(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.ChamberA:
                    return (float)Math.PI;            // 180° - left
                case EquipmentRegion.ChamberB:
                    return (float)(-Math.PI / 2);     // -90° - up
                case EquipmentRegion.ChamberC:
                    return 0f;                         // 0° - right
                case EquipmentRegion.FoupA:
                    return (float)(3 * Math.PI / 4);   // 135°
                case EquipmentRegion.FoupB:
                    return (float)(Math.PI / 4);       // 45°
                default:
                    return (float)(Math.PI / 2);       // 90° - down
            }
        }

        /// <summary>
        /// Region을 사용자 친화적인 문자열로 포맷팅
        /// </summary>
        /// <param name="region">EquipmentRegion</param>
        /// <returns>포맷팅된 문자열</returns>
        public static string FormatRegionLabel(EquipmentRegion region)
        {
            switch (region)
            {
                case EquipmentRegion.FoupA:
                    return "FOUP A";
                case EquipmentRegion.FoupB:
                    return "FOUP B";
                case EquipmentRegion.ChamberA:
                    return "Chamber A";
                case EquipmentRegion.ChamberB:
                    return "Chamber B";
                case EquipmentRegion.ChamberC:
                    return "Chamber C";
                case EquipmentRegion.TM:
                    return "TM Base";
                default:
                    return region.ToString();
            }
        }

        /// <summary>
        /// 유닛 키(PMA/PMB/PMC)를 화면에 보일 이름(Chamber A/B/C)으로 바꿉니다.
        /// 키 자체는 내부 데이터와 파일에서 그대로 쓰고, 화면 글자만 이 이름을 씁니다.
        /// </summary>
        public static string FormatUnitLabel(string unitKey)
        {
            switch (unitKey)
            {
                case "PMA":
                    return "Chamber A";
                case "PMB":
                    return "Chamber B";
                case "PMC":
                    return "Chamber C";
                default:
                    return unitKey;
            }
        }

        /// <summary>
        /// Region이 도어가 필요한지 확인
        /// </summary>
        /// <param name="region">EquipmentRegion</param>
        /// <returns>도어가 필요하면 true</returns>
        public static bool RequiresDoor(EquipmentRegion region)
        {
            return region == EquipmentRegion.ChamberA
                   || region == EquipmentRegion.ChamberB
                   || region == EquipmentRegion.ChamberC;
        }
    }
}

