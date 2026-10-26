namespace Game.Player
{
    public sealed class MonitorMailEntry
    {
        public MonitorMailMessage Definition { get; }
        public string MailId { get; }
        public string Sender { get; }
        public string Address { get; }
        public string Subject { get; }
        public string Date { get; }
        public string Body { get; }
        public bool IsResult { get; }
        public bool IsDeleted { get; internal set; }
        public KeyboardRequestDefinition Request { get; }

        /// <summary>
        /// definition의 공통 메일 정보와 조합된 body를 이번 실행의 읽기 전용 수신 기록으로 복사한다.
        /// isResult가 참이면 mailId와 customer로 결과 식별자와 고객을 지정하고 의뢰 수락 연결을 제공하지 않는다.
        /// </summary>
        internal MonitorMailEntry(MonitorMailMessage definition, string body, bool isResult,
            string mailId = null, MonitorMailMessage customer = null)
        {
            Definition = definition;
            MailId = mailId ?? definition.MailId;
            Sender = customer != null ? customer.Sender : definition.Sender;
            Address = customer != null ? customer.Address : definition.Address;
            Subject = definition.Subject;
            Date = definition.Date;
            Body = body;
            IsResult = isResult;
            KeyboardRequestDefinition request = definition.Request;
            Request = !isResult && request != null && request.InitialMail == definition ? request : null;
        }
    }
}
