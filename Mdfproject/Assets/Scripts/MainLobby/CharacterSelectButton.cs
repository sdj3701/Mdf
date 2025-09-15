using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectButton : BaseButton
{
    [SerializeField]
    private SelectCharacter selectCharacter;

    protected override void Start()
    {
        base.Start();
        //Debug.Log("where not Inspector selectCharacter script");
    }

    override public void OnClick()
    {
        // 매번 체크
        if (_gameManagers == null)
            _gameManagers = FindObjectOfType<GameManagers>();

        Button button = this.gameObject.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError($"Button 컴포넌트를 찾을 수 없습니다: {gameObject.name}");
            return;
        }
        // 2. TMP_Text 컴포넌트 가져오기
        // TODO : 나중에 큐에서 링크드 리스트로 변경할 예정
        TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();
        if (buttonText != null)
        {
            Debug.Log($"버튼 텍스트: {buttonText.text}");

            // 3. gameManagers null 체크
            if (_gameManagers != null)
            {
                _gameManagers.Pushqueue(buttonText.text);
            }
            else
            {
                Debug.LogError("gameManagers가 null입니다!");
                return;
            }

            // 4. selectCharacter null 체크
            if (selectCharacter != null)
            {
                selectCharacter.UpdateSelectCharacter();
            }
            else
            {
                Debug.LogError("selectCharacter가 null입니다!");
                return;
            }
        }
        else
        {
            // 🚨 원본 버그: buttonText가 null인데 .text 접근
            // string name = buttonText.text; ← 이게 NullReferenceException 원인!

            // ✅ 수정: null인 경우 게임오브젝트 이름 사용
            string name = gameObject.name;
            Debug.LogWarning($"'{name}' 버튼에서 TMP_Text 컴포넌트를 찾을 수 없습니다!");
        }


    }
}
