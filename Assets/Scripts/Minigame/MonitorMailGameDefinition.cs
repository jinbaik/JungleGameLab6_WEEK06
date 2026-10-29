using System;
using System.Collections.Generic;

using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(fileName = "MailApp", menuName = "Keyboard/Monitor/Mail App")]
    public sealed class MonitorMailGameDefinition : MonitorMiniGameDefinition
    {
        [Header("Inbox")]
        [SerializeField] private MonitorMailMessage[] _messages = Array.Empty<MonitorMailMessage>();

        public IReadOnlyList<MonitorMailMessage> Messages => _messages;
    }
}
