using UnityEngine;

using Game.Tutorials;

namespace KeyboardModeling.Assembly
{
    public sealed class AssemblyTutorialController : MonoBehaviour
    {
        [Header("Assembly")]
        [SerializeField] private AssemblyCatalogUI _catalog;
        [SerializeField] private AssemblyDragInput _dragInput;

        void OnEnable()
        {
            _catalog.CategorySelected += HandleCategorySelected;
            _catalog.PartSelected += HandlePartSelected;
            _catalog.PanelOpened += HandlePanelOpened;
            _catalog.BulkPlacementSelected += HandleBulkPlacementSelected;
            _dragInput.PartPicked += HandlePartPicked;
        }

        void Start()
        {
            FirstEncounterTutorial.Request(TutorialTopic.Welcome);
        }

        void OnDisable()
        {
            _catalog.CategorySelected -= HandleCategorySelected;
            _catalog.PartSelected -= HandlePartSelected;
            _catalog.PanelOpened -= HandlePanelOpened;
            _catalog.BulkPlacementSelected -= HandleBulkPlacementSelected;
            _dragInput.PartPicked -= HandlePartPicked;
        }

        /// <summary>목록을 펼치면 기본 안내 후 현재 선택 파츠 안내를 예약한다. 기본 안내를 이미 봤으면 선택 파츠 안내를 바로 요청한다.</summary>
        private void HandlePanelOpened()
        {
            if (!FirstEncounterTutorial.Request(TutorialTopic.PartsCatalog, HandleCurrentCategory))
                HandleCurrentCategory();
        }

        /// <summary>
        /// 일괄 배치 모드 선택 이벤트를 받아 WholePart 안내를 예약한다.
        /// 입력값과 반환값 없이 안내 상태만 변경하며 이미 본 안내는 다시 표시하지 않는다.
        /// </summary>
        private void HandleBulkPlacementSelected()
        {
            FirstEncounterTutorial.Request(TutorialTopic.WholePart);
        }

        /// <summary>카탈로그의 현재 선택 범주를 읽고 대응하는 파츠 안내를 예약한다. 입력값과 반환값 없이 기본 선택인 키캡도 별도 탭 클릭 없이 안내한다.</summary>
        private void HandleCurrentCategory()
        {
            if (isActiveAndEnabled) HandleCategorySelected(_catalog.CurrentCategory);
        }

        /// <summary>category에 대응하는 키캡, 스위치, 케이스 또는 보강판 안내를 예약한다. 파츠 기능이나 장착 상태는 변경하지 않는다.</summary>
        private void HandleCategorySelected(int category)
        {
            if (category == 0) FirstEncounterTutorial.Request(TutorialTopic.KeycapEditing);
            else if (category == 1) FirstEncounterTutorial.Request(TutorialTopic.AssemblySwitch);
            else if (category == 2) FirstEncounterTutorial.Request(TutorialTopic.CaseEditing);
            else if (category == 3) FirstEncounterTutorial.Request(TutorialTopic.PlateEditing);
            else if (category == 4) FirstEncounterTutorial.Request(TutorialTopic.LEDEditing);
            else if (category == 5) FirstEncounterTutorial.Request(TutorialTopic.Image);
        }

        /// <summary>선택한 entry의 종류에 따라 스위치, 일반 키캡 또는 아티산 안내를 예약한다. 파츠 수량과 장착 상태는 변경하지 않는다.</summary>
        private void HandlePartSelected(AssemblyCatalogEntry entry)
        {
            FirstEncounterTutorial.Request(entry.IsSwitch ? TutorialTopic.AssemblySwitch
                : entry.IsArtisan ? TutorialTopic.Artisan : TutorialTopic.KeycapEditing);
        }

        /// <summary>월드에서 part를 집으면 파츠 분리 안내를 예약한다. 파츠 이동과 복귀는 기존 드래그 시스템이 처리한다.</summary>
        private void HandlePartPicked(AssemblyDraggablePart part)
        {
            FirstEncounterTutorial.Request(TutorialTopic.PartRemoval);
        }
    }
}
