using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBotConsoleApp.Data
{
    public class MessageInfo
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public long ChatId { get; set; }
        public long MessageId { get; set; }
        public string? Message { get; set; }
        public DateTime MessageDate { get; set; }
    }
}
