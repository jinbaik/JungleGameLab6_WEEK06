using UnityEngine.SceneManagement;

namespace Game.Workshop
{
    /// <summary>
    /// WorkShop0에서만 전투 진입, 전투 기록과 전투용 파츠 제한을 제외한다.
    /// 공용 코드와 씬 오브젝트는 보존하며 다른 작업실 및 전투 씬은 기존 동작을 유지한다.
    /// 전투 버튼은 비활성화하고 튜토리얼은 커스터마이징 안내만 표시한다.
    /// </summary>
    public static class CustomizationOnlyWorkshop
    {
        public static bool IsActive => SceneManager.GetActiveScene().name == "WorkShop0";
    }
}
