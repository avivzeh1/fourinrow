using System;
using System.Collections.Generic;

#nullable disable

namespace grpc4InRowService.Models
{
    public partial class Game
    {
        public int GameId { get; set; }
        public int? PlayerStartedUserId { get; set; }
        public int? Player2UserId { get; set; }
        public int? WinnerUserId { get; set; }
        public int PointsPlayerStarted { get; set; }
        public int PointsPlayer2 { get; set; }
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public int CellsFilled { get; set; }

        public virtual User Player2User { get; set; }
        public virtual User PlayerStartedUser { get; set; }
        public virtual User WinnerUser { get; set; }
    }
}
