using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Tournament
{
    [CreateAssetMenu(menuName = "Game/Tournament Key Layout")]
    public sealed class TournamentKeyLayout : ScriptableObject
    {
        [Header("Explicit Rows And Columns")]
        [SerializeField] private string[] _rows =
        {
            "Escape,,F1,F2,F3,F4,F5,F6,F7,F8,F9,F10,F11,F12,,PrintScreen,ScrollLock,Pause",
            "Backquote,Digit1,Digit2,Digit3,Digit4,Digit5,Digit6,Digit7,Digit8,Digit9,Digit0,Minus,Equals,Backspace,,Insert,Home,PageUp",
            "Tab,Q,W,E,R,T,Y,U,I,O,P,LeftBracket,RightBracket,Backslash,,Delete,End,PageDown",
            "CapsLock,A,S,D,F,G,H,J,K,L,Semicolon,Quote,Enter",
            "LeftShift,Z,X,C,V,B,N,M,Comma,Period,Slash,,,RightShift,,,UpArrow",
            "LeftCtrl,LeftCommand,LeftAlt,,,,Space,,,,AltGr,RightWindows,ContextMenu,RightCtrl,,LeftArrow,DownArrow,RightArrow"
        };

        /// <summary>설정한 행의 키 이름과 빈칸을 사용해 키별 행열 좌표 사전을 생성하여 반환한다.</summary>
        public Dictionary<Key, Vector2Int> CreateCoordinates()
        {
            Dictionary<Key, Vector2Int> result = new Dictionary<Key, Vector2Int>();
            for (int row = 0; row < _rows.Length; row++)
            {
                string[] names = _rows[row].Split(',');
                for (int column = 0; column < names.Length; column++)
                {
                    if (string.IsNullOrWhiteSpace(names[column])) continue;
                    result.Add((Key)Enum.Parse(typeof(Key), names[column].Trim()), new Vector2Int(row, column));
                }
            }
            return result;
        }
    }
}
