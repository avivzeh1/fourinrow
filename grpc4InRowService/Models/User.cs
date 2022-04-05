using System;
using System.Collections.Generic;

#nullable disable

namespace grpc4InRowService.Models
{
    public partial class User
    {
        public User()
        {
            GamePlayer2Users = new HashSet<Game>();
            GamePlayerStartedUsers = new HashSet<Game>();
            GameWinnerUsers = new HashSet<Game>();
        }

        public int UserId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int GamesPlayed { get; set; }
        public int GamesWon { get; set; }
        public int GamesDraw { get; set; }
        public int Points { get; set; }

        public virtual ICollection<Game> GamePlayer2Users { get; set; }
        public virtual ICollection<Game> GamePlayerStartedUsers { get; set; }
        public virtual ICollection<Game> GameWinnerUsers { get; set; }
    }
}
