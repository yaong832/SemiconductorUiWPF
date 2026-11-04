using System;
using System.Collections.Generic;
using System.Linq;
using SemiconductorUi.Models;
using SemiconductorUi.ViewModels;

namespace SemiconductorUi.Process
{
    /// <summary>
    /// FOUP 관리 로직을 담당하는 헬퍼 클래스
    /// FOUP A/B의 웨이퍼 큐 관리, 로딩/언로딩 처리
    /// </summary>
    public class FoupManager
    {
        private readonly ProcessEngine _form;

        /// <summary>
        /// FOUP A 웨이퍼 큐
        /// </summary>
        public Queue<Wafer> FoupAQueue { get; private set; }

        /// <summary>
        /// FOUP B 완료 웨이퍼 리스트
        /// </summary>
        public List<Wafer> FoupBCompleted { get; private set; }

        /// <summary>
        /// FOUP B 완료 기준선 (언로딩 시 사용)
        /// </summary>
        public int FoupBCompletedBaseline { get; set; }

        /// <summary>
        /// FoupManager 생성자
        /// </summary>
        /// <param name="form">Form1 인스턴스</param>
        public FoupManager(ProcessEngine form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            FoupAQueue = new Queue<Wafer>();
            FoupBCompleted = new List<Wafer>();
            FoupBCompletedBaseline = 0;
        }

        /// <summary>
        /// FOUP A에 웨이퍼 로딩
        /// </summary>
        /// <param name="waferCount">로딩할 웨이퍼 수</param>
        /// <param name="secondExposureEnabled">2차 노광 활성화 여부</param>
        public void LoadWafersToFoupA(int waferCount, bool secondExposureEnabled)
        {
            if (FoupAQueue == null)
            {
                FoupAQueue = new Queue<Wafer>();
            }
            else
            {
                FoupAQueue.Clear();
            }

            // 설정된 개수만큼 웨이퍼를 큐에 추가
            for (int i = 1; i <= waferCount; i++)
            {
                FoupAQueue.Enqueue(new Wafer(i, secondExposureEnabled));
            }
        }

        /// <summary>
        /// FOUP A에서 웨이퍼 제거 (다음 웨이퍼 반환)
        /// </summary>
        /// <returns>다음 웨이퍼, 없으면 null</returns>
        public Wafer DequeueWaferFromFoupA()
        {
            if (FoupAQueue == null || FoupAQueue.Count == 0)
            {
                return null;
            }

            return FoupAQueue.Dequeue();
        }

        /// <summary>
        /// FOUP A에서 웨이퍼 확인 (제거하지 않음)
        /// </summary>
        /// <returns>다음 웨이퍼, 없으면 null</returns>
        public Wafer PeekWaferFromFoupA()
        {
            if (FoupAQueue == null || FoupAQueue.Count == 0)
            {
                return null;
            }

            return FoupAQueue.Peek();
        }

        /// <summary>
        /// FOUP A에 웨이퍼가 있는지 확인
        /// </summary>
        /// <returns>웨이퍼가 있으면 true</returns>
        public bool HasWafersInFoupA()
        {
            return FoupAQueue != null && FoupAQueue.Count > 0;
        }

        /// <summary>
        /// FOUP A의 웨이퍼 수
        /// </summary>
        /// <returns>웨이퍼 수</returns>
        public int GetFoupACount()
        {
            return FoupAQueue?.Count ?? 0;
        }

        /// <summary>
        /// FOUP B에 완료 웨이퍼 추가
        /// </summary>
        /// <param name="wafer">완료된 웨이퍼</param>
        public void AddCompletedWaferToFoupB(Wafer wafer)
        {
            if (wafer == null)
            {
                return;
            }

            if (FoupBCompleted == null)
            {
                FoupBCompleted = new List<Wafer>();
            }

            wafer.CurrentStage = "FOUP B";
            // 1층부터 적재 (리스트 끝에 추가하여 순서대로 쌓임)
            FoupBCompleted.Add(wafer);
        }

        /// <summary>
        /// FOUP B 언로딩: 완료 웨이퍼를 설비 밖으로 배출하여 카운트 초기화
        /// </summary>
        /// <returns>언로딩된 웨이퍼 수</returns>
        public int UnloadFoupB()
        {
            var completedCount = FoupBCompleted?.Count ?? 0;
            if (completedCount > 0)
            {
                FoupBCompleted.Clear();
            }
            return completedCount;
        }

        /// <summary>
        /// FOUP B의 완료 웨이퍼 수
        /// </summary>
        /// <returns>완료 웨이퍼 수</returns>
        public int GetFoupBCount()
        {
            return FoupBCompleted?.Count ?? 0;
        }

        /// <summary>
        /// FOUP B에 웨이퍼가 있는지 확인
        /// </summary>
        /// <returns>웨이퍼가 있으면 true</returns>
        public bool HasWafersInFoupB()
        {
            return FoupBCompleted != null && FoupBCompleted.Count > 0;
        }

        /// <summary>
        /// FOUP A/B 표시용 카운트 가져오기
        /// </summary>
        /// <param name="displayFoupACount">FOUP A 카운트</param>
        /// <param name="displayFoupBCount">FOUP B 카운트</param>
        public void GetFoupDisplayCounts(out int displayFoupACount, out int displayFoupBCount)
        {
            // 공정 진행 여부와 무관하게 "현재 상태"를 표시
            // A: 실제 큐에 있는 웨이퍼 수 (1층부터 빼가므로 정확함)
            // B: 완료 리스트 개수 (1층부터 적재되므로 정확함)
            displayFoupACount = GetFoupACount();
            displayFoupBCount = GetFoupBCount();
        }

        /// <summary>
        /// 특정 층에 웨이퍼가 있는지 확인 (FOUP A)
        /// </summary>
        /// <param name="layer">층 번호 (1~5)</param>
        /// <returns>해당 층에 웨이퍼가 있으면 true</returns>
        public bool HasWaferAtLayerFoupA(int layer)
        {
            if (FoupAQueue == null || FoupAQueue.Count == 0)
            {
                return false;
            }

            var waferArray = FoupAQueue.ToArray();
            return waferArray.Any(w => w.Id == layer);
        }

        /// <summary>
        /// 특정 층에 웨이퍼가 있는지 확인 (FOUP B)
        /// </summary>
        /// <param name="layer">층 번호 (1~5)</param>
        /// <returns>해당 층에 웨이퍼가 있으면 true</returns>
        public bool HasWaferAtLayerFoupB(int layer)
        {
            if (FoupBCompleted == null || FoupBCompleted.Count == 0)
            {
                return false;
            }

            return FoupBCompleted.Any(w => w.Id == layer);
        }

        /// <summary>
        /// FOUP A/B 상태 초기화
        /// </summary>
        public void Reset()
        {
            if (FoupAQueue != null)
            {
                FoupAQueue.Clear();
            }
            if (FoupBCompleted != null)
            {
                FoupBCompleted.Clear();
            }
            FoupBCompletedBaseline = 0;
        }
    }
}

