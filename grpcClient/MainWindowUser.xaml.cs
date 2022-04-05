using Grpc.Core;
using grpc4InRowService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace grpcClient
{
    /// <summary>
    ///
    /// </summary>
    public partial class MainWindowUser : Window
    {
        public MainWindowUser()
        {
            InitializeComponent();
        }
        /// <summary>
        /// request that received and sent by this user
        /// </summary>
        List<GameRequest> activeRequests = new List<GameRequest>();   

        public Fourinrow.FourinrowClient Client { get; set; }
        public string Username { get; set; }
        /// <summary>
        /// listen to meassages from the server of type GameRequest
        /// </summary>
        /// <param name="stream">the stream which the messages are through</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task ListenAsync(IAsyncStreamReader<GameRequest> stream, CancellationToken token)
        {

            await foreach (GameRequest info in stream.ReadAllAsync(token))
            {
                if (info.Type == MessageType.Update)
                {
                    if (info.ToUser != "") // a user is out of the waiting room
                    {
                        BoolModel res = await Client.IsOutOfSystemAsync(new UserInfo { UserName = info.ToUser }); //check if he is out of system or playing

                        if (res.Answer == true) // the user is logged out from the system
                        {
                            for (int i = 0; i < activeRequests.Count; i++) // remove active requests interacted with this user
                            {  
                                if (activeRequests.ElementAt(i).ToUser == info.ToUser || activeRequests.ElementAt(i).FromUser == info.ToUser)
                                {
                                    activeRequests.RemoveAt(i);
                                    i--; // check this index again after remove
                                }
                            }
                        }
                    }
                    await UpdateUsersListAsync();
                }
                else if (info.Type == MessageType.Request) // game request was received
                {
                    activeRequests.Add(info);
                    lbRequests.ItemsSource = (from r in activeRequests  // update list box of requests
                                             where r.FromUser != Username  // just the requests which was received
                                             select new { r.FromUser }.FromUser).ToList();
                }
                else if (info.Type == MessageType.Accepted) // a request that was sent by this user was accepted
                {
                    foreach (var req in activeRequests)  // remove the request from the list
                        if (req.ToUser == info.FromUser)  // the player that this player sent request to is accepted so remove this request from active requests
                        {
                            activeRequests.Remove(req);
                            break;
                        }

                    try
                    {  // try to start a game
             //           await Client.DisconnectAsync(new UserInfo { UserName = Username }); // get out from waiting room

                        await Client.AddGameAsync(new GameModel // add the game to the database and to the list of the server for manage this game
                        {
                            Player1 = info.ToUser,  // player1 is the player to start the game, he got the message that player2 accept his game request
                            Player2 = info.FromUser
                        });

                        AsyncServerStreamingCall<Move> listener = Client.ConnectGame(new UserInfo { UserName = Username }); // connect the game
                        GameWindow gameWindow = new GameWindow();
                        gameWindow.Client = Client;
                        gameWindow.Username = Username;
                        gameWindow.First = Username; // the player to start
                        gameWindow.Second = info.FromUser; // the player who doesn't start
                        gameWindow.Title = Username;
                        gameWindow.Player2 = info.FromUser; // the rival
                        gameWindow.Turn = Username; // initialize the turn to the first player to play
                        gameWindow.Listen += ListenAsync;  // delegate for get back listening to game requsets at the end of the game
                        gameWindow.UpdateMainWindow += UpdateUsersListAsync; // delegate for update list box of connected users while playing
                        gameWindow.UpdateRequests += RemoveFromActiveRequests; // delegate for remove request that this player sent and it was declined while playing
                        gameWindow.InitBoard(); // prepare the board
                        gameWindow.ListenAsync(listener.ResponseStream, new CancellationTokenSource().Token); // start listening to moves
                        gameWindow.Show();
                    }
                    catch (RpcException ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                }
                else if (info.Type == MessageType.Declined)  // a request that this player sent was declined
                {
                    foreach (var req in activeRequests)  // remove the request
                        if (req.ToUser == info.FromUser)  // the player that this player sent request to declined, so remove this request from active requests
                        {
                            activeRequests.Remove(req);
                            break;
                        }
                }
            }
        }
        /// <summary>
        /// update the list box of connected users
        /// </summary>
        /// <returns></returns>
        private async Task UpdateUsersListAsync()
        {
            var users = (await Client.UpdateUsersAsync(new Empty())).UserNames;
            users.Remove(Username);
            lbUsers.ItemsSource = users;
            lbRequests.ItemsSource = (from r in activeRequests  // update list box of requests
                                      where r.FromUser != Username  // just the requests which was received
                                      select new { r.FromUser }.FromUser).ToList();
        }
        /// <summary>
        ///  send a game request to selected user from the list box of connected users
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonSend_Click(object sender, RoutedEventArgs e)
        {
            if (lbUsers.SelectedItem == null)
            {
                MessageBox.Show("You must select a user to send the message to",Username);
                return;
            }
            string receiver = lbUsers.SelectedItem as string;
            try
            {
                if (activeRequests.Count > 0)
                {
                    foreach (var req in activeRequests)  
                    {
                        if (req.ToUser == receiver) // if the selected user already has a request from this user
                        {
                            MessageBox.Show("You have already sent to this player a request", Username);
                            return;
                        }

                        if (req.FromUser == receiver) // if the selected user has already sent a request to this user 
                        {
                            MessageBox.Show("You have already received a request from this player", Username);
                            return;
                        }
                    }
                }

                GameRequest gr = new GameRequest
                {
                    Type = MessageType.Request,
                    FromUser = Username,
                    ToUser = receiver
                };

                await Client.SendRequestAsync(gr);  // send the game request to the selected user
                activeRequests.Add(gr);  // add to active requests
                MessageBox.Show("Request was sent successfully", Username);
            }
            catch (RpcException ex)
            {
                if (ex.Message != new RpcException(new Status(StatusCode.Unavailable, "User is not connected to the waiting room or you are already playing")).Message 
                    && ex.Message != new RpcException(new Status(StatusCode.Unavailable, "You are already playing")).Message
                    && ex.Message != new RpcException(new Status(StatusCode.Unavailable, "User is already playing")).Message)
                    MessageBox.Show("Connection with the server is lost", Username); // if is unknown message the server probably crashed
                else
                    MessageBox.Show(ex.Message); // display the specific message
            }
        }
        /// <summary>
        /// closing the mainWindow means disconnecting from the server
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Window_Closed(object sender, EventArgs e)
        {
            await Client.DisconnectAsync(new UserInfo { UserName = Username });
            Environment.Exit(Environment.ExitCode);
        }
        /// <summary>
        /// open serach window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void searchWindow_Click(object sender, RoutedEventArgs e)
        {
            SearchWindow searchwindow = new SearchWindow();
            searchwindow.Client = Client;
            searchwindow.UpdateUsersListBox();
            searchwindow.Show();
        }
        /// <summary>
        /// event handler of mouse double click on selected item in requests list box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ResponseToRequest(object sender, MouseButtonEventArgs e)
        {
            string req = lbRequests.SelectedItem as string;

            foreach (var r in activeRequests) // search the request
            {
                if (r.FromUser == req) // request was found
                {
                    var result = MessageBox.Show(
                                              "Game request from " + r.FromUser, "Request for " + Username,
                                              MessageBoxButton.YesNo,
                                              MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes) // accept the request
                    {
                        try
                        {
                            await Client.SendRequestAsync(new GameRequest  // notify the user who sent the request that it was accepted 
                            {
                                Type = MessageType.Accepted,
                                FromUser = Username,
                                ToUser = r.FromUser
                            });
                        }
                        catch (RpcException ex) // if the other player is already playing or the user is already playing
                        {
                            MessageBox.Show(ex.Message);
                            return;  // if the accepted notify fail, return for leave the request on the list box for option to try later
                        }

                        activeRequests.Remove(r);  
                        lbRequests.ItemsSource = (from rq in activeRequests
                                                  where rq.FromUser != Username
                                                  select new { rq.FromUser }.FromUser).ToList(); // update requests list box

              //          await Client.DisconnectAsync(new UserInfo { UserName = Username });

                        AsyncServerStreamingCall<Move> listener =
                            Client.ConnectGame(new UserInfo { UserName = Username });  // connect the game
                        GameWindow gameWindow = new GameWindow();
                        gameWindow.Client = Client;
                        gameWindow.Username = Username;
                        gameWindow.First = r.FromUser;  // the player to start is the player who sent request
                        gameWindow.Second = Username;  // the player who doesn't start
                        gameWindow.Title = Username;
                        gameWindow.Player2 = r.FromUser; // the rival
                        gameWindow.Turn = r.FromUser; // initialize the turn to the first player to play
                        gameWindow.Listen += ListenAsync;  // delegate for get back listening to game requsets at the end of the game
                        gameWindow.UpdateMainWindow += UpdateUsersListAsync; // delegate for update list box of connected users while playing
                        gameWindow.UpdateRequests += RemoveFromActiveRequests; // delegate for remove request that this player sent and it was declined while playing
                        gameWindow.InitBoard(); // prepare the board
                        gameWindow.ListenAsync( listener.ResponseStream, new CancellationTokenSource().Token);
                        gameWindow.Show();
                    }

                    else  // decline the request
                    {
                        activeRequests.Remove(r);
                        lbRequests.ItemsSource = (from rq in activeRequests
                                                  where rq.FromUser != Username
                                                  select new { rq.FromUser }.FromUser).ToList();  // update requests list box
                        try
                        {
                            await Client.SendRequestAsync(new GameRequest   // notify the user who sent the request that it was declined 
                            {
                                Type = MessageType.Declined,
                                FromUser = Username,
                                ToUser = r.FromUser
                            });
                        }
                        catch (RpcException ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }
                    return;
                }
            }
        }
        /// <summary>
        /// remove request that sent by this user because the other player declined the request while this player
        /// is playing. this function called by delegate in game window
        /// </summary>
        /// <param name="userWhoDeclined"> to this parameter has a property Player that represents the player that declined the request</param>
        public void RemoveFromActiveRequests(Move userWhoDeclined)
        {        
            foreach(var req in activeRequests) // search the request to remove
            {
                if (req.ToUser == userWhoDeclined.Player || req.FromUser == userWhoDeclined.Player) {  // request that this player sent to the player who declined
                    activeRequests.Remove(req);
                    return;
                }
            }
        }
    }
}
