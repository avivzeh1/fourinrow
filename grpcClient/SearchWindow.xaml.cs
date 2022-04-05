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
    /// Interaction logic for SearchWindow.xaml
    /// </summary>
    public partial class SearchWindow : Window
    {
        public SearchWindow()
        {
            InitializeComponent();
            lbData.ItemTemplate = null;
            Players = null;
        }

        public Fourinrow.FourinrowClient Client { get; set; }
        private UsersModel Players { get; set; }
        /// <summary>
        /// set item source for sorting ways combo box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Initialized(object sender, EventArgs e)
        {
            List<string> sortings = new List<string>();
            sortings.Add("By username");
            sortings.Add("By games played");
            sortings.Add("By winnings");
            sortings.Add("By losses");
            sortings.Add("By points");
            sortPlayersBy.ItemsSource = sortings;

        }
        /// <summary>
        /// update list box of connetced users
        /// </summary>
        public async void UpdateUsersListBox()
        {
            try
            {
                var users = await Client.UpdateUsersAsync(new Empty());
                lbPlayers.ItemsSource = users.UserNames;
            }
            catch (Exception )
            {
                MessageBox.Show("Connection with server is lost");
            }
        }
        /// <summary>
        /// get all users statistics from the server and display it in data list box order by the selected sorting way from combo box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonShowAllPlayers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                lbData.ItemTemplate = (DataTemplate)mainGrid.Resources["UserTemplate"]; // the appropriate data template
                Players = await Client.GetUsersStatisticsAsync(new Empty());
                SortUsers();
                lbPercentage.ItemsSource = null;
            }
            catch (Exception)
            {
                MessageBox.Show("Connection with server is lost");
            }
        }

        /// <summary>
        /// different sorting way for users was chosen, sort the users by the chosen way
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void sortPlayersChangeHandler(object sender, SelectionChangedEventArgs e)
        {
            if (lbData.ItemTemplate == null || lbData.ItemTemplate != (DataTemplate)mainGrid.Resources["UserTemplate"] 
                || Players == null) // if in data list box something different from users is displayed
                return; //
          
            SortUsers();  // call the sort function
        }
        /// <summary>
        /// sort the users by the seleted sorting way in combo box
        /// </summary>
        private void SortUsers()
        {
            string sort = (string)sortPlayersBy.SelectedItem;

            switch (sort)
            {
                case "By username":
                    lbData.ItemsSource = Players.Players.OrderBy(u => u.Username);
                    break;
                case "By games played":
                    lbData.ItemsSource = Players.Players.OrderByDescending(u => u.GamesPlayed);
                    break;
                case "By winnings":
                    lbData.ItemsSource = Players.Players.OrderByDescending(u => u.GamesWon);
                    break;
                case "By losses":
                    lbData.ItemsSource = Players.Players.OrderByDescending(u => u.GamesLose);
                    break;
                case "By points":
                    lbData.ItemsSource = Players.Players.OrderByDescending(u => u.Points);
                    break;
                default: lbData.ItemsSource = Players.Players.OrderBy(u => u.Username);
                    break;
            }
        }
        /// <summary>
        /// get all games played data from the server and display it in data list box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonShowAllGamesPlayed_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                lbData.ItemTemplate = (DataTemplate)mainGrid.Resources["GamePlayedTemplate"];  // the appropriate data template
                GamesModel games = await Client.GetGamesPlayedStatisticsAsync(new Empty());
                lbData.ItemsSource = games.Games.OrderBy(g => g.Date);  // order by date
                lbPercentage.ItemsSource = null;
            }
            catch (Exception)
            {
                MessageBox.Show("Connection with server is lost");
            }
        }
        /// <summary>
        /// get all current games details from the server and display it in data list box
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonShowCurrentGames_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                lbData.ItemTemplate = (DataTemplate)mainGrid.Resources["CurrentGameTemplate"];  // the appropriate data template
                GamesModel games = await Client.GetCurrentGamesStatisticsAsync(new Empty());
                lbData.ItemsSource = games.Games.OrderBy(g => g.Date);  // order by time
                lbPercentage.ItemsSource = null;
            }
            catch (Exception)
            {
                MessageBox.Show("Connection with server is lost");
            }
        }
        /// <summary>
        /// if there is 1 selected player in connected list box, display its data in data list box
        /// if there are 2 selected players display all the games data between both of them
        /// else display an error message
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonlbPlayers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (lbPlayers.SelectedItems.Count == 0)
                {
                    MessageBox.Show("You must select a player for showing data", "Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (lbPlayers.SelectedItems.Count > 2)
                {
                    MessageBox.Show("You can't select more than 2 players for getting data", "Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (lbPlayers.SelectedItems.Count == 1) // display the selected player data in data list box
                {
                    string selectedPlayer = lbPlayers.SelectedItem as string;
                    List<UserData> data = new List<UserData>();
                    UserData ud = await Client.GetUserDataAsync(new UserInfo { UserName = selectedPlayer });
                    data.Add(ud);
                    lbData.ItemTemplate = (DataTemplate)mainGrid.Resources["UserDataTemplate"];  // the appropriate data template
                    lbData.ItemsSource = data;
                    lbPercentage.ItemsSource = null;
                    return;
                }

                if (lbPlayers.SelectedItems.Count == 2)
                {
                    string player12 = "";

                    foreach (string selectedPlayer in lbPlayers.SelectedItems) // create string with delimiter ":" => player1:player2
                    {
                        player12 += selectedPlayer + ":"; // user name doesn't include ":" for sure because of the restriction user name
                    }
                    string[] playersArr = player12.Split(':');

                    string player1 = playersArr[0];
                    string player2 = playersArr[1];

                    lbData.ItemTemplate = (DataTemplate)mainGrid.Resources["GamePlayedTemplate"];  // the appropriate data template
                    GamesModel games = await Client.GetDuelDataAsync(new GameModel { Player1 = player1, Player2 = player2 }); // get all the duels of the two players
                    lbData.ItemsSource = games.Games.OrderBy(g => g.Date); // the item source is the Games property which include a list of GameModel
                   // show winning percentage for each player
                    double winnings1 = (from g in games.Games
                                     where g.Winner == player1
                                     select g).Count();
                    double winnings2 = (from g in games.Games
                                     where g.Winner == player2
                                     select g).Count();
                    double percentage1 = games.Games.Count != 0 ? (winnings1 / games.Games.Count)*100 : 0;
                    double percentage2 = games.Games.Count != 0 ? (winnings2 / games.Games.Count)*100 : 0;
                    PercentageModel pm = new PercentageModel
                    {
                        Player1 = player1,
                        Player2 = player2,
                        Percentage1 = percentage1.ToString().Length >= 5 ? percentage1.ToString().Substring(0, 5) + "%" : percentage1.ToString() + "%",  // 2 digits after point
                        Percentage2 = percentage2.ToString().Length >= 5 ? percentage2.ToString().Substring(0, 5) + "%" : percentage2.ToString() + "%"  // 2 digits after point
                    };
                    List<PercentageModel> pml = new List<PercentageModel>();  // list for create item source  to list box
                    pml.Add(pm);
                    lbPercentage.ItemsSource = pml;
                    return;
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Connection with server is lost");
            }
        }
               
    }
}
