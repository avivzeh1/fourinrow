using Grpc.Core;
using Grpc.Net.Client;
using grpc4InRowService;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace grpcClient
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        /// <summary>
        /// user name restriction by regular expression
        /// </summary>
        private Regex userNamePattern = new Regex(@"^([a-zA-Z]|[0-9]|!|&|#|\?|\s){1,30}$");
        public static Fourinrow.FourinrowClient Client { get; private set; }
        public static string Username { get; private set; }
        /// <summary>
        /// connect to grpc server with username and password
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            if (AllTextBoxesFilled())
            {
                var channel = GrpcChannel.ForAddress("https://localhost:5001");
                Fourinrow.FourinrowClient client = new Fourinrow.FourinrowClient(channel);
                string username = tbUsername.Text.Trim();
                string password = tbPassword.Text.Trim();

                try
                {
                    await client.UserNotExistsAsync(new UserInfo { UserName = username });
                    client.ValidUser(new UserInfo { UserName = username, Password = password });
                    AsyncServerStreamingCall<GameRequest> listener =
                    client.Connect(new UserInfo { UserName = username, Password = password });
                    MainWindowUser mainWindow = new MainWindowUser();
                    mainWindow.Client = client;
                    mainWindow.Username = username;
                    mainWindow.Title = username;
                    mainWindow.ListenAsync(
                        listener.ResponseStream, new CancellationTokenSource().Token);
                    this.Close();
                    mainWindow.Show();
                   
                }
                catch (RpcException ex)
                {
                    // user is not exist, register or wrong password
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK,   // sub string for showing only the message
                        MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                MessageBox.Show("You must fill all data", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// check if all text boxes in the window are filled
        /// </summary>
        /// <returns>true if all text boxes are filled, else false</returns>
        private bool AllTextBoxesFilled()
        {
            foreach (var item in mainGrid.Children)
            {
                if (item is TextBox)
                {
                    if (string.IsNullOrEmpty((item as TextBox).Text))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
        /// <summary>
        ///  register to the database with username and password
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void registerButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AllTextBoxesFilled())
            {
                MessageBox.Show("You must fill all data", "Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            if (!userNamePattern.IsMatch(tbUsername.Text.Trim()))
            {
                MessageBox.Show("User name can include only numbers, letters or the characters [?&#!]", "Error", MessageBoxButton.OK,
                     MessageBoxImage.Error);
                return;
            }
            if (tbPassword.Text.Length < 8)
            {
                MessageBox.Show("Password must contain at least 8 characters", "Error", MessageBoxButton.OK,
                     MessageBoxImage.Error);
                return;
            }

            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            Fourinrow.FourinrowClient client = new Fourinrow.FourinrowClient(channel);
            string username = tbUsername.Text.Trim();
            string password = tbPassword.Text.Trim();

            try
            {
                await client.UserExistsAsync(new UserInfo { UserName = username });
            }
            catch (RpcException ex)
            {
                // user is exist, use another name
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, 
                  MessageBoxImage.Error);
                return;
            }

            try
            {
                await client.InsertAsync(new UserModel // insert user to database
                {
                    Username = tbUsername.Text.Trim(),
                    Password = tbPassword.Text.Trim(),
                    GamesPlayed = 0,
                    GamesWon = 0
                });
                MessageBox.Show("User was added successfully");
            }
            catch (RpcException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
        
    }
}
