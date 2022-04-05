using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace grpc4InRowService.Models
{
    public partial class fourinrow_avivContext : DbContext
    {
        public fourinrow_avivContext()
        {
        }

        public fourinrow_avivContext(DbContextOptions<fourinrow_avivContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Game> Games { get; set; }
        public virtual DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
                optionsBuilder.UseSqlServer("Data Source=(LocalDB)\\MSSQLLocalDB;Initial Catalog= fourinrow_aviv;AttachDbFilename=C:\\fourinrow\\fourinrow_aviv.mdf;Integrated Security=True;Connect Timeout=30;");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "SQL_Latin1_General_CP1_CI_AS");

            modelBuilder.Entity<Game>(entity =>
            {
                entity.HasIndex(e => e.Player2UserId, "IX_Games_Player2UserId");

                entity.HasIndex(e => e.PlayerStartedUserId, "IX_Games_PlayerStartedUserId");

                entity.HasIndex(e => e.WinnerUserId, "IX_Games_WinnerUserId");

                entity.HasOne(d => d.Player2User)
                    .WithMany(p => p.GamePlayer2Users)
                    .HasForeignKey(d => d.Player2UserId);

                entity.HasOne(d => d.PlayerStartedUser)
                    .WithMany(p => p.GamePlayerStartedUsers)
                    .HasForeignKey(d => d.PlayerStartedUserId);

                entity.HasOne(d => d.WinnerUser)
                    .WithMany(p => p.GameWinnerUsers)
                    .HasForeignKey(d => d.WinnerUserId);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
