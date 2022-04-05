using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace fourinrowDB
{

    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int GamesPlayed { get; set; }
        public int GamesWon { get; set; }
        public int GamesDraw { get; set; }
        public int Points { get; set; }
        public IEnumerable<Game> UserGamesStart { get; set; }
        public IEnumerable<Game> UserGamesNotStart { get; set; } 
        public IEnumerable<Game> UserGamesWon { get; set; }

    }

    public class Game
    {
        public int GameId { get; set; }
        public User PlayerStarted { get; set; }
        public User Player2 { get; set; }
        public User Winner { get; set; }
        public int PointsPlayerStarted { get; set; }
        public int PointsPlayer2 { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public int CellsFilled { get; set; }
    }
    public class FourinrowContext : DbContext
    {
        private const string ConnectionString =
        @"Data Source=(LocalDB)\MSSQLLocalDB;" +
            @"Initial Catalog= fourinrow_aviv;" +
            @"AttachDbFilename=C:\fourinrow\fourinrow_aviv.mdf;" +
            @"Integrated Security=True;Connect Timeout=30";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.UseSqlServer(ConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // User to Game (starting player) (1 to many)
            modelBuilder.Entity<User>()
                .HasMany(u => u.UserGamesStart)
                .WithOne(g => g.PlayerStarted);

            // User to Game (player2) (1 to many)
            modelBuilder.Entity<User>()
                .HasMany(u => u.UserGamesNotStart)
                .WithOne(g => g.Player2);

            // User to Game (Winner) 1 to many
            modelBuilder.Entity<User>()
                .HasMany(u => u.UserGamesWon)
                .WithOne(g => g.Winner);
        }
        public DbSet<User> Users { get; set; }
        public DbSet<Game> Games { get; set; }

    }
    class Program
    {
        static async Task Main(string[] args)
        {
            await Create();
        }

        private static async Task Create()
        {
            using (var context = new FourinrowContext())
            {
                bool created = await context.Database.EnsureCreatedAsync();
                string creationInfo = created ? "created" : "exists";
                Console.WriteLine($"database {creationInfo}");
            }
        }
    }
}
