using Grpc.Core;
using grpc4InRowService.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Google.Protobuf.WellKnownTypes;

namespace grpc4InRowService
{
    public class FourinrowService : Fourinrow.FourinrowBase
    {
        private readonly ILogger<FourinrowService> _logger;

        public FourinrowService(ILogger<FourinrowService> logger)
        {
            _logger = logger;
            try
            {  // if in any prevoious running, the server was crashed while games are played, 
                // mark their end time for not showing them in current games after 24 hours since the start time
                // so that it won't disturb the games being played today
                using (var ctx = new fourinrow_avivContext())
                {
                    List<Game> prevGames = (from g in ctx.Games
                                            where g.End == null && g.Start < DateTime.Now.AddHours(-24)
                                            select g).ToList();

                    var playerStartedUser = ctx.Games.Include(ga => ga.PlayerStartedUser).ToList();
                    var player2User = ctx.Games.Include(ga => ga.Player2User).ToList();

                    if (prevGames.Count > 0)
                    {
                        foreach (var g in prevGames)
                            g.End = DateTime.Now;
                        ctx.SaveChanges();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }
        }
        /// <summary>
        /// connected users to waiting room. key - username, value his messages of GameRequest type
        /// </summary>
        private static ConcurrentDictionary<string, List<GameRequest>> users = new ConcurrentDictionary<string, List<GameRequest>>();
        /// <summary>
        /// users which are playing. key - username, value his messages of Move type
        /// </summary>
        private static ConcurrentDictionary<string, List<Move>> players = new ConcurrentDictionary<string, List<Move>>();
        /// <summary>
        /// The current games which are being played
        /// </summary>
        private static List<GameModel> games = new List<GameModel>();
        /// <summary>
        /// boolean veriable for do operations of reading and writing on database only one time when the server is up
        /// </summary>
    //    bool isInitialized = false;

        /// <summary>
        /// the interval which sets the checking of messages
        /// </summary>
        private static readonly TimeSpan interval = TimeSpan.FromSeconds(0.5);
        /// <summary>
        /// connect the user to the waiting room
        /// </summary>
        /// <param name="user"> the user to connect</param>
        /// <param name="responseStream"> the returned stream</param>
        /// <param name="context"></param>
        /// <returns> a stream of GameRequest</returns>
        public override async Task Connect(UserInfo user,
            IServerStreamWriter<GameRequest> responseStream,
            ServerCallContext context)
        {      
            users.TryAdd(user.UserName, new List<GameRequest>());  // add user to waiting room
            var token = context.CancellationToken;
            InformAllUsers(new GameRequest  // update all users that new user connected to waiting room
            {
                Type = MessageType.Update,
                FromUser = "",
                ToUser = ""
            });
            InformAllPlayers(new Move
            {
                Type = MoveType.Updateplayers,
                Player = ""
            });
            while (!token.IsCancellationRequested)
            {
                if (users[user.UserName].Count > 0)
                {
                    foreach (var item in users[user.UserName])  // if there are any request messages
                    {
                        await responseStream.WriteAsync(item);  // write requests to stream
                    }
                }
                users[user.UserName].Clear();
                await Task.Delay(interval, token);
            }
        }
        /// <summary>
        /// connect the user to the game
        /// </summary>
        /// <param name="user">the user to connect</param>
        /// <param name="responseStream"> the returned stream</param>
        /// <param name="context"></param>
        /// <returns>a stream of Move</returns>
        public override async Task ConnectGame(UserInfo user, IServerStreamWriter<Move> responseStream,
            ServerCallContext context)
        {

            players.TryAdd(user.UserName, new List<Move>());  // add the user to player list

            var val = new List<GameRequest>();
            users.TryRemove(user.UserName, out val);  // try remove from users (disconnect from the waiting room)

            InformAllUsers(new GameRequest  // notify the users that the user disconnect from waiting room
            {
                Type = MessageType.Update,
                FromUser = "",
                ToUser = "" 
            });
            InformAllPlayers(new Move 
            {
                Type = MoveType.Updateplayers,
                Player = ""  // there is no need to update active requests
            });

            var token = context.CancellationToken;

            while (!token.IsCancellationRequested)
            {
                if (players[user.UserName].Count > 0) // if there are any move messages
                {
                    foreach (var item in players[user.UserName])
                    {
                        await responseStream.WriteAsync(item); // write moves to stream
                    }
                }
                players[user.UserName].Clear();
                await Task.Delay(interval, token);
            }
        }
        /// <summary>
        /// disconnect the user from waiting room
        /// </summary>
        /// <param name="user">the user to disconnect</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> Disconnect(UserInfo user, ServerCallContext context)
        {
            var val = new List<GameRequest>();
            users.TryRemove(user.UserName, out val);  // try remove from users
            var val2 = new List<Move>();
            players.TryRemove(user.UserName, out val2); // if disconnect while playing, remove from players 
            InformAllUsers(new GameRequest  // notify the users that the user disconnect from waiting room
            {
                Type = MessageType.Update,
                FromUser = "",
                ToUser = user.UserName  // inform the users which user disconnected for handle the situation of active requests for this user, and the user logged out of system
            });
            InformAllPlayers(new Move
            {
                Type = MoveType.Updateplayers,
                Player = user.UserName // inform the players which user disconnected for handle the situation of active requests for this user, and the user logged out of system
            });
            if (games.Count > 0 )
            {
                foreach (var g in games) // search the game that the user who disconnected was participating
                {
                    if (g.Player1 == user.UserName)  // game was found
                    {
                        players[g.Player2].Add(new Move  // send a message to the other player that his rival disconnected
                        {
                            Type = MoveType.Disconnect,
                            Player = g.Player1,  // the player who disconnected
                            Against = g.Player2, // the other player
                            Col = 0,
                            X = 0,
                            Y = 0
                        });
                        break;
                    }
                    if (g.Player2 == user.UserName) // game was found
                    {
                        players[g.Player1].Add(new Move   // send a message to the other player that his rival disconnected
                        {
                            Type = MoveType.Disconnect,
                            Player = g.Player2, // the player who disconnected
                            Against = g.Player1,// the other player
                            Col = 0,
                            X = 0,
                            Y = 0
                        });
                        break;
                    }
                }
            }
            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// check if user is in players list.  call by the client when the client was informed that user is disconnected from the waiting room
        /// </summary>
        /// <param name="user">the user to check</param>
        /// <param name="context"></param>
        /// <returns>true if user is in players list, else false</returns>
        public override async Task<BoolModel> IsOutOfSystem(UserInfo user, ServerCallContext context)
        {
            if (players.ContainsKey(user.UserName) || users.ContainsKey(user.UserName))  // user is still in the system
                return await Task.FromResult(new BoolModel{ Answer = false });
            else
                return await Task.FromResult(new BoolModel { Answer = true });
        }
        /// <summary>
        /// disconnet user from game
        /// </summary>
        /// <param name="user">the user to disconnect</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> DisconnectGame(UserInfo user, ServerCallContext context)
        {
            var val = new List<Move>();
            players.TryRemove(user.UserName, out val); // remove from players list, note that the game was removed from the curren games list when the winner was set
            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// send a move to the players in the game
        /// </summary>
        /// <param name="move">the move to send</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> Play(Move move, ServerCallContext context)
        {
            if (players.ContainsKey(move.Against)) //  send the move to rival
            {
                players[move.Against].Add(move);
            }
            else
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Connection with the player is lost"));
            }
            if (players.ContainsKey(move.Player))  // send the player who executed the move also
            {
                players[move.Player].Add(move);
            }
            else
            {
                if(move.Type != MoveType.Retire) // not relevant for retire move
                    throw new RpcException(new Status(StatusCode.NotFound, "Your Connection in the game is lost"));
            }
            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// add new game to database
        /// </summary>
        /// <param name="game"> the game to add</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> AddGame(GameModel game, ServerCallContext context)
        {
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {

                    User playerStarted = (from u in ctx.Users
                                          where u.Username == game.Player1
                                          select u).First();
                    User player2 = (from u in ctx.Users
                                           where u.Username == game.Player2
                                           select u).First();
                    playerStarted.GamesPlayed++;
                    player2.GamesPlayed++;
                    Game newGame = new Game
                    {
                        PlayerStartedUser = playerStarted,
                        Player2User = player2,
                        Start = DateTime.Now
                    };
                    ctx.Games.Add(newGame);
                    ctx.SaveChanges();
                }
            }
            catch (Exception ex)
            {              
                throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
            }
            games.Add(game);
            return await Task.FromResult(new Empty());

        }
        /// <summary>
        /// get user data of user
        /// </summary>
        /// <param name="user">the user to get his data</param>
        /// <param name="context"></param>
        /// <returns>User data</returns>
        public override async Task<UserData> GetUserData(UserInfo user, ServerCallContext context)
        {
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    User wanted = (from u in ctx.Users
                                   where u.Username == user.UserName
                                   select u).First();
                   double percentage =  ((double)wanted.GamesWon / wanted.GamesPlayed) * 100; //win percentage
                    var reply = new UserData
                    {
                        Username = wanted.Username,
                        NumberOfGames = wanted.GamesPlayed,
                        NumberOfWinnings = wanted.GamesWon,
                        WinPercentage = percentage.ToString().Length >= 5 ? percentage.ToString().Substring(0,5) + "%" : percentage.ToString() + "%",  // 2 digits after point
                        Points = wanted.Points

                    };
                    return await Task.FromResult(reply);
                }
            }
            catch (RpcException ex)
            {
                throw new RpcException(new Status(StatusCode.AlreadyExists, ex.Message));
            }

        }
        /// <summary>
        /// compute SHA256 hash of the string
        /// </summary>
        /// <param name="rawData"> the string to compute the hash</param>
        /// <returns>the hash of the input string</returns>
        private string ComputeSha256Hash(string rawData)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
        /// <summary>
        /// add the input request to all users in waiting room
        /// </summary>
        /// <param name="gameRequest">the request to add for each user</param>
        private void InformAllUsers(GameRequest gameRequest)
        {
            if (users.Keys.Count > 0)
            {
                foreach (var item in users.Keys)
                {
                    users[item].Add(gameRequest);
                }
            }
        }

        private void InformAllPlayers(Move move)
        {
            if (players.Keys.Count > 0)
            {
                foreach (var item in players.Keys)
                {
                    players[item].Add(move);
                }
            }
        }

        /// <summary>
        /// add game request to the user who the request is intended for
        /// </summary>
        /// <param name="req">the request to send</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> SendRequest(GameRequest req, ServerCallContext context)
        {
            if (req.Type == MessageType.Update)
            {
                InformAllUsers(req);
            }
            else if (req.Type == MessageType.Declined)  // response to active request
            {
                if (users.ContainsKey(req.ToUser)) // if the user that declined is not playing, update him that his request was declined
                    users[req.ToUser].Add(req);
                else if (players.ContainsKey(req.ToUser)) // the user is playing so update him in a middle of his game by move
                {
                    players[req.ToUser].Add(new Move
                    {
                        Type = MoveType.Updateplayers,
                        Player = req.FromUser  // the player who declined the request
                    });
                }
                return await Task.FromResult(new Empty());

            }
            else if (req.Type == MessageType.Accepted && players.ContainsKey(req.ToUser)) // if a user accept a request of a busy player
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "User is already playing"));
            }
            else if (req.Type == MessageType.Accepted && players.ContainsKey(req.FromUser))  // if a user accept a request while he is playing
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "You are already playing"));
            }

            if (users.ContainsKey(req.ToUser) && !players.ContainsKey(req.FromUser))  // if both of the players are connected to the waiting room
            {
                users[req.ToUser].Add(req);
            }
            else
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "User is not connected to the waiting room or you are already playing"));
            }
            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// if user is not exist in data base exception was thrown
        /// </summary>
        /// <param name="user">the user to check if exist in data base</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> UserNotExists(UserInfo user, ServerCallContext context)
        {
            bool isExist;
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                     isExist = (from u in ctx.Users
                                where user.UserName == u.Username
                                select u).ToList().Count > 0;
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }

            if (!isExist)
                throw new RpcException(new Status(StatusCode.NotFound,
           "User is not exist, please press the register button for adding your user name to the database"));

            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// update the data base fields of the game after its ending
        /// </summary>
        /// <param name="gr">the game to update</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> SetWinner(GameResult gr, ServerCallContext context)
        {
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    if(games.Count > 0) {
                        foreach (var g in games) // search the game in the list of current games
                        {
                            if (g.Player1 == gr.PlayerStarted && g.Player2 == gr.Player2) // game was found
                            {
                                var gamesDB = from ga in ctx.Games
                                              select ga;
                                // include all users refernces for get the user names
                                var playerStartedUser = ctx.Games.Include(ga => ga.PlayerStartedUser).ToList();
                                var player2User = ctx.Games.Include(ga => ga.Player2User).ToList();

                                List<Game> possiblegame = (from ga in ctx.Games  // search this game in database to update fields
                                                   where g.Player1 == ga.PlayerStartedUser.Username && g.Player2 == ga.Player2User.Username
                                                   && ga.End == null
                                                   select ga).OrderBy(game => game.Start).ToList();
                                if(possiblegame.Count == 0) // check if the game was updated already
                                    return await Task.FromResult(new Empty());

                               Game game = possiblegame.Last();  // the last game between the two players
                                //note that if the server was crashed while those two players were playing
                                //the data base may include another games between the two players without end time stamp which will
                                // have an end time stamp after 24 hours. because of that, the game to update is the last game in the list
                                if (gr.Winner != "") // there is a winner
                                {
                                    User winner = (from u in ctx.Users
                                                   where u.Username == gr.Winner
                                                   select u).First();
                                    winner.GamesWon++;
                                    game.WinnerUser = winner;
                                }
                                else  // draw
                                {
                                    User user1 = (from u in ctx.Users
                                                  where u.Username == gr.PlayerStarted
                                                  select u).First();
                                    user1.GamesDraw++;

                                    User user2 = (from u in ctx.Users
                                                  where u.Username == gr.Player2
                                                  select u).First();
                                    user2.GamesDraw++;
                                }
                                // update fields
                                game.PointsPlayerStarted = gr.PointsPlayerStarted;
                                game.PointsPlayer2 = gr.PointsPlayer2;
                                game.PlayerStartedUser.Points += gr.PointsPlayerStarted;
                                game.Player2User.Points += gr.PointsPlayer2;
                                game.CellsFilled = gr.CellsFilled;
                                game.End = DateTime.Now;
                                ctx.SaveChanges();
                                games.Remove(g); //remove from current games after update for not update the same game twice
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }

            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// check if password is appropriate to the user name, if not exception was thrown
        /// </summary>
        /// <param name="user">the user to check</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> ValidUser(UserInfo user, ServerCallContext context)
        {
            bool isExist;
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    string hash = ComputeSha256Hash(user.Password);
                    isExist = (from u in ctx.Users
                               where user.UserName == u.Username && hash == u.Password
                               select u).ToList().Count > 0;
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.DataLoss, ex.Message));
            }

            if (!isExist)
                throw new RpcException(new Status(StatusCode.NotFound, "Password/Username is wrong"));

            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// check if user name is already exists, if it does exception was thrown
        /// </summary>
        /// <param name="user">the user to check</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> UserExists(UserInfo user, ServerCallContext context)
        {
            bool isExist;
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                     isExist = (from u in ctx.Users
                                    where user.UserName == u.Username
                                    select u).ToList().Count > 0;
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.DataLoss, ex.Message));
            }

            if (isExist)
                throw new RpcException(new Status(StatusCode.InvalidArgument,"User already exists"));

            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// return the user names list of connected users to the waiting room
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>a list of a user names wrap by type Users</returns>
        public override async Task<Users> UpdateUsers(Empty request,ServerCallContext context)
        {
            var reply = new Users  // wrap the list by return type
            {
                UserNames = { users.Keys }
            };
            return await Task.FromResult(reply);
        }
        /// <summary>
        /// return a list of UserModel type which include some data of the users
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>a list of UserModel wrap by UsersModel type</returns>
        public override async Task<UsersModel> GetUsersStatistics(Empty request, ServerCallContext context)
        {
            List<UserModel> players = new List<UserModel>();
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    List<User> users_ = (from u in ctx.Users
                                        select u).ToList(); // all the user in data base

                    foreach (var u in users_)
                        players.Add(new UserModel  // create UserModel with the required data for each user
                        {
                            Username = u.Username,
                            GamesPlayed = u.GamesPlayed,
                            GamesWon = u.GamesWon,
                            GamesLose = u.GamesPlayed - u.GamesWon - u.GamesDraw,
                            Points = u.Points
                        });
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.DataLoss, ex.Message));
            }
            var reply = new UsersModel  // wrap the list by return type
            {
                Players = { players }
            };
            return await Task.FromResult(reply);
        }
        /// <summary>
        /// return a list of GameModel type which include some data of the games
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>a list of GameModel wrap by GamesModel type</returns>
        public override async Task<GamesModel> GetGamesPlayedStatistics(Empty request, ServerCallContext context)
        {
            List<GameModel> games_ = new List<GameModel>();
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    List<Game> gamesDB = (from g in ctx.Games
                                          where g.End != null  // if there is no end time stamp the game is currently being played
                                          select g).ToList();  // games played

                    // include the users references for get their fields
                    var playerStartedUser = ctx.Games.Include(ga => ga.PlayerStartedUser).ToList();
                    var player2User = ctx.Games.Include(ga => ga.Player2User).ToList();
                    var winnerUser = ctx.Games.Include(ga => ga.WinnerUser).ToList();

                    if (gamesDB.Count > 0)
                    {
                        foreach (var g in gamesDB)
                            games_.Add(new GameModel  // create GameModel with the required data for each game
                            {
                                Player1 = g.PlayerStartedUser.Username,
                                Player2 = g.Player2User.Username,
                                Winner = g.WinnerUser != null ? g.WinnerUser.Username : "Draw",
                                PointsPlayerStarted = g.PointsPlayerStarted,
                                PointsPlayer2 = g.PointsPlayer2,
                                Date = g.Start.ToShortDateString().ToString()
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.DataLoss, ex.Message));
            }
            var reply = new GamesModel   // wrap the list by return type
            {
                Games = { games_ }
            };
            return await Task.FromResult(reply);
        }
        /// <summary>
        /// return a list of current games, a list of GameModel type which include some data of a games which are being played currently
        /// </summary>
        /// <param name="request"></param>
        /// <param name="context"></param>
        /// <returns>a list of GameModel wrap by GamesModel type</returns>
        public override async Task<GamesModel> GetCurrentGamesStatistics(Empty request, ServerCallContext context)
        {
            List<GameModel> games_ = new List<GameModel>();
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    List<Game> gamesDB = (from g in ctx.Games
                                          where g.End == null
                                          select g).ToList();  // current games
                    // include the users references for get their fields
                    var playerStartedUser = ctx.Games.Include(ga => ga.PlayerStartedUser).ToList();
                    var player2User = ctx.Games.Include(ga => ga.Player2User).ToList();

                    if (gamesDB.Count > 0)
                    {
                        foreach (var g in gamesDB)
                            games_.Add(new GameModel  // create GameModel with the required data for each game
                            {
                                Player1 = g.PlayerStartedUser.Username,
                                Player2 = g.Player2User.Username,
                                Date = g.Start.ToShortTimeString() // time without date
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.DataLoss, ex.Message));
            }
            var reply = new GamesModel  // wrap the list by return type
            {
                Games = { games_ }
            };
            return await Task.FromResult(reply);
        }
        /// <summary>
        /// insert a new user to data base
        /// </summary>
        /// <param name="user2add">the user to add to data base</param>
        /// <param name="context"></param>
        /// <returns></returns>
        public override async Task<Empty> Insert(UserModel user2add, ServerCallContext context)
        {
            User newUser = new User
            {
                Username = user2add.Username,
                Password = ComputeSha256Hash(user2add.Password),
                GamesPlayed = 0,
                GamesWon = 0
            };
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    ctx.Users.Add(newUser);
                    ctx.SaveChanges();
                }
            }
            catch (RpcException ex)
            {
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }

            return await Task.FromResult(new Empty());
        }
        /// <summary>
        /// return a list of GameModel type which include all the duels of the two players 
        /// </summary>
        /// <param name="duel">the object include properties Player1 and Player2 which are the two players to return their duels</param>
        /// <param name="context"></param>
        /// <returns>a list of GameModel wrap by GamesModel type</returns>
        public override async Task<GamesModel> GetDuelData(GameModel duel, ServerCallContext context)
        {
            List<GameModel> games_ = new List<GameModel>();
            try
            {
                using (var ctx = new fourinrow_avivContext())
                {
                    // include all users refernces for get the user names
                    var playerStartedUser = ctx.Games.Include(ga => ga.PlayerStartedUser).ToList();
                    var player2User = ctx.Games.Include(ga => ga.Player2User).ToList();

                    List<Game> gamesDB = (from g in ctx.Games
                                          where (g.PlayerStartedUser.Username == duel.Player2 && g.Player2User.Username == duel.Player1)
                                          || (g.PlayerStartedUser.Username == duel.Player1 && g.Player2User.Username == duel.Player2)
                                          select g).ToList(); // all the duels of the two players 

                    if (gamesDB.Count > 0)
                    {
                        foreach (var g in gamesDB)
                            games_.Add(new GameModel  // create GameModel with the required data for each game
                            {
                                Player1 = g.PlayerStartedUser.Username,
                                Player2 = g.Player2User.Username,
                                Winner = g.WinnerUser != null ? g.WinnerUser.Username : "Draw",
                                PointsPlayerStarted = g.PointsPlayerStarted,
                                PointsPlayer2 = g.PointsPlayer2,
                                Date = g.Start.ToShortDateString().ToString()
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
            var reply = new GamesModel  // wrap the list by return type
            {
                Games = { games_ }
            };
            return await Task.FromResult(reply);
        }
    }
}
