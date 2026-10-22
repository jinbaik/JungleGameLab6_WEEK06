using System;
using System.Collections.Generic;

using UnityEngine;

namespace Game.Tutorials
{
    [CreateAssetMenu(fileName = "TutorialContent", menuName = "Keyboard/Tutorial Content")]
    public sealed class TutorialContent : ScriptableObject
    {
        [Header("Tutorial Cards")]
        [SerializeField] private TutorialCard[] _cards = Array.Empty<TutorialCard>();

        public IReadOnlyList<TutorialCard> Cards => _cards;
    }
}
