using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace TelegramBotConsoleApp.Data
{
    public class TelegramContext : DbContext
    {

        public DbSet<MessageInfo> MessageInfos { get; set; }

        public TelegramContext(DbContextOptions<TelegramContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new MessageInfoMap());

            base.OnModelCreating(modelBuilder);
        }
    }
}
