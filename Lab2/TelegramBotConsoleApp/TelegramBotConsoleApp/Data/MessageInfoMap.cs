using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramBotConsoleApp.Data
{
    public class MessageInfoMap : IEntityTypeConfiguration<MessageInfo>
    {
        public void Configure(EntityTypeBuilder<MessageInfo> builder)
        {
            builder.HasKey(message => message.Id);

            builder.Property(message => message.Message)
                .IsRequired(false)
                .HasMaxLength(4100);

            builder.Property(message => message.MessageDate)
                .IsRequired();

            builder.Property(product => product.ChatId)
                .IsRequired();

            builder.Property(product => product.UserId)
                .IsRequired();

            builder.Property(product => product.MessageId)
                .IsRequired();
        }
    }
}
