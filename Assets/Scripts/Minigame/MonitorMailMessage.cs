using UnityEngine;

namespace Game.Player
{
    [CreateAssetMenu(fileName = "MailMessage", menuName = "Keyboard/Monitor/Mail Message")]
    public sealed class MonitorMailMessage : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _mailId;
        [SerializeField] private KeyboardRequestDefinition _request;

        [Header("Envelope")]
        [SerializeField] private string _sender;
        [SerializeField] private string _address;
        [SerializeField] private string _subject;
        [SerializeField] private string _date;

        [Header("Message")]
        [SerializeField, TextArea(8, 24)] private string _body;

        public string MailId => _mailId;
        public KeyboardRequestDefinition Request => _request;
        public string Sender => _sender;
        public string Address => _address;
        public string Subject => _subject;
        public string Date => _date;
        public string Body => _body;
    }
}
