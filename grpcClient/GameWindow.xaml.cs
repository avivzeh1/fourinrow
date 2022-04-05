using Grpc.Core;
using grpc4InRowService;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace grpcClient
{
    /// <summary>
    /// The window that the user play a game
    /// </summary>
    public partial class GameWindow : Window
    {
        /// <summary>
        /// this class represent a moving circle with position (x,y) on window
        /// </summary>
        class Ball
        {
            public Ellipse Circle { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double XMove { get; set; }
            public double YMove { get; set; }
        }

        /// <summary>
        /// delegate for update the list box of thhe connected player in the main window while playing
        /// </summary>
        public Func<Task> UpdateMainWindow { get; internal set; }

        public delegate void CreateDelegate(Move m);
        /// <summary>
        /// delegate for update the active requests list in the main window while playing in case of a request that this user sent was declined
        /// </summary>
        public event CreateDelegate UpdateRequests;
        /// <summary>
        /// delegate for start listening to a game requests in the main window
        /// </summary>
        public Func<IAsyncStreamReader<GameRequest>, CancellationToken, Task> Listen { get; internal set; }
        /// <summary>
        /// Time limit timer to play the turn      
        /// </summary>
        private DispatcherTimer timer;
        /// <summary>
        /// initialized to 30 when the timer starts and decrease by 1 each tick of the timer
        /// </summary>
        int timerStart;
        private int[,] board = new int[6, 7];
        /// <summary>
        /// This array represents the free row in each column on board, for example
        /// boardinfo[0] = 5 means that if the player pick the first column it will take over the last row and boardinfo[0] will be 4
        /// </summary>
        private int[] boardinfo = new int[7] { 5, 5, 5, 5, 5, 5, 5 };
        /// <summary>
        /// the point of the player who started the game
        /// </summary>
        private int pointsPlayerStarted = 0;
        /// <summary>
        /// the point of the player who didn't start the game
        /// </summary>
        private int pointsPlayer2 = 0;
        /// <summary>
        /// amount of cells which were filled
        /// </summary>
        private int cellsFilled = 0;
        /// <summary>
        /// ellipse that displays on the window above the columns
        /// </summary>
        public Ellipse el;
        public Fourinrow.FourinrowClient Client { get; set; }
        /// <summary>
        /// user name of this player
        /// </summary>
        public string Username { get; set; }
        /// <summary>
        /// this property is the name of the player which its turn to play
        /// </summary>
        public string Turn { get; set; }
        /// <summary>
        /// The started player
        /// </summary>
        public string First { get; set; }
        /// <summary>
        /// The player who didn't start
        /// </summary>
        public string Second { get; set; }
        /// <summary>
        /// The player that this user play with
        /// </summary>
        public string Player2 { get; set; }
        /// <summary>
        /// this field will turn to true in any situation of game ending for prevent events handlers to work while game was ended
        /// </summary>
        private bool isWinning = false;
        /// <summary>
        /// boolean variable for synchronize with the timer
        /// </summary>
        private bool isPlay = false;
        /// <summary>
        /// constuctor 
        /// </summary>
        public GameWindow()
        {
            InitializeComponent();
            isWinning = false;
            el = new Ellipse
            {
                Height = 55,
                Width = 55,
                Fill = Brushes.Red
            };
            // set the position of the ellipse and add to the top of the window (row1)
            Canvas.SetTop(el, 40 - el.Height / 2);
            Canvas.SetLeft(el, 100 - el.Width / 2);
            row1.Children.Add(el);

        }
        /// <summary>
        /// listen to meassages from the server of type Move
        /// </summary>
        /// <param name="stream">the stream which the messages are through</param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task ListenAsync(IAsyncStreamReader<Move> stream, CancellationToken token)
        {
            await foreach (Move info in stream.ReadAllAsync(token))
            {

                if (info.Type == MoveType.Updateplayers) // need to update list box or active requests in main window
                {
                    if (info.Player != "") // user disconnected or declined a request that this player sent to him
                        UpdateRequests(info); // use delegate for update active requests in main window
                    await UpdateMainWindow(); //the list of connected users was updated, use delegate for update list box in main window

                }
                if (info.Type == MoveType.Play)  // play the move
                {
                    await Play(info);
                }
                else if (info.Type == MoveType.Retire) // the other player retired
                {
                    isWinning = true;
                    if (info.Player != Username) // don't show to the retired player
                    {
                        if (timer != null)
                            timer.Stop();
                        tbTimer.Visibility = Visibility.Hidden;
                        tbTurn.Text = info.Player + " is retired. You won!";
                        tbTurn.Foreground = Brushes.Red;
                    }
                    DisconnectGame();
                }
                else if (info.Type == MoveType.Disconnect) // the other player disconnect from the server
                {
                    isWinning = true;
                    if (timer != null)
                        timer.Stop();
                    tbTimer.Visibility = Visibility.Hidden;
                    if (Username == First)
                        await Client.SetWinnerAsync(new GameResult
                        {
                            Winner = First,
                            PlayerStarted = First,
                            Player2 = Second,
                            PointsPlayerStarted = 1000 + Bonus(1),  // winning score, the other player disconnected
                            PointsPlayer2 = pointsPlayer2 + Bonus(2),
                            CellsFilled = cellsFilled
                        });
                    if (Username == Second)
                        await Client.SetWinnerAsync(new GameResult
                        {
                            Winner = Second,
                            PlayerStarted = First,
                            Player2 = Second,
                            PointsPlayerStarted = pointsPlayerStarted + Bonus(1),
                            PointsPlayer2 = 1000 + Bonus(2),
                            CellsFilled = cellsFilled
                        });
                    tbTurn.Text = "Connection with " + info.Player + " is lost. You won!";
                    tbTurn.Foreground = Brushes.Red;
                    DisconnectGame();
                }
            }
        }
        /// <summary>
        /// call the server services for disconnect game and connect back to the waiting room
        /// </summary>
        private async void DisconnectGame()
        {
            await Client.DisconnectGameAsync(new UserInfo { UserName = Username });
            AsyncServerStreamingCall<GameRequest> listener =
                Client.Connect(new UserInfo { UserName = Username });
            Listen(listener.ResponseStream, new CancellationTokenSource().Token);
        }
        /// <summary>
        /// check if there is at least 1 piece in each column.
        /// </summary>
        /// <param name="p"> represent the player to check the bonus of. 1 is the started player, 2 is the other player</param>
        /// <returns>return 100 if there is at least 1 piece in each column, else return 0</returns>
        private int Bonus(int p)
        {
            bool atLeast1 = false;

            for (int i = 0; i < 7; i++)
            {
                for (int j = 0; j < 6; j++)
                    if (board[j, i] == p)
                    {
                        atLeast1 = true;
                        break;
                    }

                if (!atLeast1)
                    return 0;

                atLeast1 = false;
            }

            return 100;
        }
        /// <summary>
        /// check if the game board is full
        /// </summary>
        /// <returns> return true if all the columns on the game board are full, else rerurn false</returns>
        private bool AllBoardFilled()
        {
            for (int i = 0; i < 7; i++)
                if (boardinfo[i] > -1)
                    return false;
            return true;
        }
        /// <summary>
        /// check if there are connected 4 values equal to x
        /// </summary>
        /// <param name="x"> the value to check connected 4</param>
        /// <param name="i"> the row that of the new piece to check</param>
        /// <param name="j">the column of the new piece to check</param>
        /// <returns>return true if there are 4 pieces connected,else return false </returns>
        private bool isWin(int x, int i, int j)
        {
            int count = 0;
            // Horizontal check
            for (int col = 0; col < 7; col++)
            {
                if (board[i, col] == x)
                    count++;
                else
                    count = 0;

                if (count == 4)
                    return true;
            }
            //Vertical check
            count = 0;
            for (int row = 0; row < 6; row++)
            {
                if (board[row, j] == x)
                    count++;
                else
                    count = 0;

                if (count == 4)
                    return true;
            }

            // top-left to bottom-right diagonal, bottom-left part
            count = 0;
            for (int rowStart = 0; rowStart < 3; rowStart++) // for all possible rows from left
            {
                count = 0;
                int row, col;
                for (row = rowStart, col = 0; row < 6 && col < 7; row++, col++)
                {
                    if (board[row, col] == x)
                        count++;
                    else
                        count = 0;

                    if (count == 4)
                        return true;
                }
            }

            // top-left to bottom-right diagonal, top-right part
            count = 0;
            for (int colStart = 1; colStart < 4; colStart++) // for each possible column
            {
                count = 0;
                int row, col;
                for (row = 0, col = colStart; row < 6 && col < 7; row++, col++)
                {
                    if (board[row, col] == x)
                        count++;
                    else
                        count = 0;

                    if (count == 4)
                        return true;

                }
            }
            // top-right to bottom-left diagonal, top-left part
            count = 0;
            for (int colStart = 3; colStart < 7; colStart++) // for each possible column
            {
                count = 0;
                int row, col;
                for (row = 0, col = colStart; row < 6 && col >= 0; row++, col--)
                {
                    if (board[row, col] == x)
                        count++;
                    else
                        count = 0;

                    if (count == 4)
                        return true;

                }
            }
            // top-right to bottom-left diagonal, bottom-right part
            count = 0;
            for (int rowStart = 1; rowStart < 3; rowStart++) // for each possible row
            {
                count = 0;
                int row, col;
                for (row = rowStart, col = 6; row < 6 && col >= 0; row++, col--)
                {
                    if (board[row, col] == x)
                        count++;
                    else
                        count = 0;

                    if (count == 4)
                        return true;

                }
            }
            return false;
        }
        /// <summary>
        /// Draw the board game 
        /// </summary>
        public void InitBoard()
        {
            Ellipse[,] boardEllipses = new Ellipse[6, 7];
            tbTurn.Text = Turn + "'s turn";
            SolidColorBrush color = Brushes.White;
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 7; j++)
                {
                    boardEllipses[i, j] = new Ellipse
                    {
                        Height = 55,
                        Width = 55,
                        Fill = color
                    };
                    Ball newBall = new Ball  // wrap the ellipse with ball object for set position on window
                    {
                        Circle = boardEllipses[i, j],
                        X = 60 + 113 * j - boardEllipses[i, j].Width / 2,
                        Y = 60 + 70 * i - boardEllipses[i, j].Height / 2,
                    };
                    // set postion on canvas and add the ball
                    Canvas.SetTop(boardEllipses[i, j], newBall.Y);
                    Canvas.SetLeft(boardEllipses[i, j], newBall.X);
                    myCanvas.Children.Add(boardEllipses[i, j]);

                }
            }
            if (First == Username) // set timer to the player who starts
                SetTimer();
        }
        /// <summary>
        /// execute the move on the board game
        /// </summary>
        /// <param name="info"> details of the move</param>
        /// <returns></returns>
        private async Task Play(Move info)
        {
            try
            {

                Ellipse e = new Ellipse  // create the ball 
                {
                    Height = 55,
                    Width = 55,
                    Fill = el.Fill
                };
                Ball newBall = new Ball
                {
                    Circle = e,
                    X = info.X - e.Width / 2,
                    Y = info.Y - e.Height / 2,
                    YMove = 70
                };
                Canvas.SetTop(e, newBall.Y);  // set postion and add to canvas
                Canvas.SetLeft(e, newBall.X);
                myCanvas.Children.Add(e);

                ThreadPool.QueueUserWorkItem(MoveBallBouncing, newBall);  //drop animation in background

                if (info.Player == First)  // points for the move
                    pointsPlayerStarted += 10;
                if (info.Player == Second)
                    pointsPlayer2 += 10;

                cellsFilled++;
                board[boardinfo[info.Col], info.Col] = el.Fill == Brushes.Red ? 1 : 2; // mark the right cell on matrix
                boardinfo[info.Col]--;

                if (AllBoardFilled() && !isWin(1, boardinfo[info.Col] + 1, info.Col) && !isWin(2, boardinfo[info.Col] + 1, info.Col))  // draw
                {
                    isWinning = true;
                    timer.Stop();
                    tbTimer.Visibility = Visibility.Hidden;
                    tbTurn.Text = "Game over in a draw!";
                    tbTurn.Foreground = Brushes.Red;
                    try
                    {
                        await Client.SetWinnerAsync(new GameResult
                        {
                            Winner = "",
                            PlayerStarted = First,
                            Player2 = Second,
                            PointsPlayerStarted = pointsPlayerStarted + Bonus(1),
                            PointsPlayer2 = pointsPlayer2 + Bonus(2),
                            CellsFilled = cellsFilled
                        });
                        DisconnectGame();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                    return;
                }
                if (el.Fill == Brushes.Red && isWin(1, boardinfo[info.Col] + 1, info.Col)) // red winning, the player who started
                {
                    isWinning = true;
                    timer.Stop();
                    tbTimer.Visibility = Visibility.Hidden;
                    tbTurn.Text = First + " won!";
                    tbTurn.Foreground = Brushes.Red;
                    await Client.SetWinnerAsync(new GameResult
                    {
                        Winner = First,
                        PlayerStarted = First,
                        Player2 = Second,
                        PointsPlayerStarted = 1000 + Bonus(1),  // winning points 
                        PointsPlayer2 = pointsPlayer2 + Bonus(2),
                        CellsFilled = cellsFilled
                    });
                    DisconnectGame();
                    return;
                }
                if (el.Fill == Brushes.Yellow && isWin(2, boardinfo[info.Col] + 1, info.Col)) // yellow winning, the player who didn't start
                {
                    isWinning = true;
                    timer.Stop();
                    tbTimer.Visibility = Visibility.Hidden;
                    tbTurn.Text = Second + " won!";
                    tbTurn.Foreground = Brushes.Red;
                    await Client.SetWinnerAsync(new GameResult
                    {
                        Winner = Second,
                        PlayerStarted = First,
                        Player2 = Second,
                        PointsPlayerStarted = pointsPlayerStarted + Bonus(1),
                        PointsPlayer2 = 1000 + Bonus(2),
                        CellsFilled = cellsFilled
                    });
                    DisconnectGame();
                    return;
                }
                if (info.Player == Username) // switch turn
                {
                    Turn = Player2;
                    tbTurn.Text = Player2 + "'s turn!";
                }
                else
                {
                    isPlay = false; // wait for play a move
                    Turn = Username;
                    tbTurn.Text = Username + "'s turn!";
                }
                if (el.Fill == Brushes.Red) // switch the color of the turn ellipse
                {
                    el.Fill = Brushes.Yellow;
                    if (Second == Username) // set timer for the turn
                    {
                        SetTimer();
                        tbTimer.Visibility = Visibility.Visible;
                    }
                    else if (timer != null) // stop timer from the previous turn
                    {
                        timer.Stop();
                        tbTimer.Visibility = Visibility.Hidden;
                    }
                }
                else // it was a yellow turn, switch to red
                {
                    el.Fill = Brushes.Red;
                    if (First == Username) // set timer for the turn
                    {
                        SetTimer();
                        tbTimer.Visibility = Visibility.Visible;
                    }
                    else if (timer != null) // stop timer from the previous turn
                    {
                        timer.Stop();
                        tbTimer.Visibility = Visibility.Hidden;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }
        /// <summary>
        ///  move the turn ellipse to the roght column according to the mouse move
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MouseMove_Event(object sender, MouseEventArgs e)
        {
            if (isWinning) // game over
                return;
            Point p = Mouse.GetPosition(mainGrid);
            el.Visibility = Visibility.Hidden; // hide the prevoiuse circle
            double x = getColX(p.X); // get the x value of the column
            Canvas.SetTop(el, 40 - el.Height / 2); // constant y
            Canvas.SetLeft(el, x - el.Width / 2); // set x
            el.Visibility = Visibility.Visible; // display the new

        }
        /// <summary>
        /// calculate the appropriate value of column's x cordinate
        /// </summary>
        /// <param name="x"> value of x cordinate </param>
        /// <returns> return the value of x cordinate of the column which appropriate to the value of x</returns>
        private int getColX(double x)
        {
            if (x < 115)
                return 60;
            if (x >= 115 && x < 230)
                return 173;
            if (x >= 230 && x < 345)
                return 286;
            if (x >= 345 && x < 460)
                return 399;
            if (x >= 460 && x < 575)
                return 512;
            if (x >= 575 && x < 690)
                return 625;
            if (x >= 690 && x < 840)
                return 738;
            return 0;
        }
        /// <summary>
        /// calculate the index of column according to the x value
        /// </summary>
        /// <param name="x"> value of x cordinate on window</param>
        /// <returns> the index of column on board matrix</returns>
        private int getColumn(double x)
        {
            if (x < 115)
                return 0;
            if (x >= 115 && x < 230)
                return 1;
            if (x >= 230 && x < 345)
                return 2;
            if (x >= 345 && x < 460)
                return 3;
            if (x >= 460 && x < 575)
                return 4;
            if (x >= 575 && x < 690)
                return 5;
            if (x >= 690 && x < 840)
                return 6;
            return 0;
        }
        /// <summary>
        /// event of mouse down means the player played a move
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void MouseDown_click(object sender, MouseButtonEventArgs e)
        {

            if (isWinning) // game over
                return;

            isPlay = true;
            Point p = Mouse.GetPosition(mainGrid); // get the position on window of the click
            try
            {
                if (Turn == Username) // if it is the user turn
                {
                    if (boardinfo[getColumn(p.X)] == -1)  // column is full
                    {
                        MessageBox.Show("Column is full");
                        return;
                    }

                    await Client.PlayAsync(new Move   // send to the server the etails of the move
                    {
                        Type = MoveType.Play,
                        Player = Username,  // the player who made a move
                        Against = Player2, // the rival
                        Col = getColumn(p.X), // which column the player picked
                        X = getColX(p.X), // x cordinate value of column
                        Y = 60 // start from height of 60
                    });

                    Turn = Player2; // switch turn

                }
            }
            catch (RpcException ex)
            {
                if (ex.Message != new RpcException(new Status(StatusCode.NotFound, "Connection with the player is lost")).Message
                        && ex.Message != new RpcException(new Status(StatusCode.NotFound, "Your Connection in the game is lost")).Message)
                {
                    if (timer != null)
                        timer.Stop();
                    tbTimer.Visibility = Visibility.Hidden;
                    MessageBox.Show("Connection with the server is lost", Username); // if it unknown message, the server probably crashed
                    return;
                }
                if (timer != null)
                    timer.Stop();
                tbTimer.Visibility = Visibility.Hidden;
                MessageBox.Show(ex.Message); // show the specific message
            }
        }
        /// <summary>
        /// drop the ball to the appropriate position on board
        /// </summary>
        /// <param name="obj"></param>
        private void MoveBallBouncing(object obj)
        {
            var ball = obj as Ball;
            while (ball.Y < boardinfo[getColumn(ball.X)] * 70 + 60) // while y smaller than the y value destination
            {
                Thread.Sleep(120);
                ball.Y += ball.YMove;
                Dispatcher.Invoke(() =>
                {
                    Canvas.SetTop(ball.Circle, ball.Y);
                    Canvas.SetLeft(ball.Circle, ball.X);
                });
            }
        }
        /// <summary>
        /// window closing means that the player who closed is retired
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                if (isWinning) // game over
                    return;

                if (timer != null)
                    timer.Stop();
                tbTimer.Visibility = Visibility.Hidden;

                await Client.PlayAsync(new Move  //send retired message to the server for notify the other player
                {
                    Type = MoveType.Retire,
                    Player = Username, // the player who retired
                    Against = Player2, // the other player
                    Col = 0,
                    X = 0,
                    Y = 0

                });
                //set the winner, check if the winner is the first to play or the second for get appropriate points
                if (Username == First)  // if the started player is retired
                    await Client.SetWinnerAsync(new GameResult
                    {
                        Winner = Second,
                        PlayerStarted = First,
                        Player2 = Second,
                        PointsPlayerStarted = pointsPlayer2 + Bonus(1),  // the points were given to the player who retired
                        PointsPlayer2 = 1000 + Bonus(2), // winning points to other player
                        CellsFilled = cellsFilled
                    });
                if (Username == Second) // if the player who didn't start is retired
                    await Client.SetWinnerAsync(new GameResult
                    {
                        Winner = First,
                        PlayerStarted = First,
                        Player2 = Second,
                        PointsPlayerStarted = 1000 + Bonus(1),  // winning points to other player
                        PointsPlayer2 = pointsPlayerStarted + Bonus(2),   // the points were given to the player who retired
                        CellsFilled = cellsFilled
                    });
                DisconnectGame();
            }
            catch (RpcException ex)
            {
                if (ex.Message != new RpcException(new Status(StatusCode.NotFound, "Connection with the player is lost")).Message
                    && ex.Message != new RpcException(new Status(StatusCode.NotFound, "Your Connection in the game is lost")).Message)
                {
                    MessageBox.Show("Connection with the server is lost"); // if it is unknown exception, the server probably crashed
                    return;
                }
                else
                    MessageBox.Show(ex.Message); // display the specific message

            }
        }
        /// <summary>
        /// define new timer for turn and start it
        /// </summary>
        private void SetTimer()
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timerStart = 30; // from 30 to 0
            timer.Tick += OnTimerTick; // on timer tick handler
            timer.Start();

        }
        /// <summary>
        ///  update timer text and check of it is the final countdown, if the time is up the player counts as retired
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void OnTimerTick(object sender, EventArgs e)
        {
            if (isWinning) //game over
                return;

            tbTimer.Text = (--timerStart).ToString(); // update text
            if (timerStart < 11)
                tbTimer.Foreground = Brushes.Red; // text in red for the last 10 seconds
            else
                tbTimer.Foreground = Brushes.Black;
            if (tbTimer.Text == "0") // time is up
            {
                if (isPlay) // if the user played
                    return;

                isWinning = true;
                timer.Stop();
                tbTurn.Text = "You run out of time. You lost!";
                tbTurn.Foreground = Brushes.Red;
                try
                {
                    await Client.PlayAsync(new Move  //send retired message to the server for notify the other player
                    {
                        Type = MoveType.Retire,
                        Player = Username,  // the player who retired
                        Against = Player2, // the other player
                        Col = 0,
                        X = 0,
                        Y = 0

                    });
                    //set the winner, check if the winner is the first to play or the second for get appropriate points
                    if (Username == First)  // if the started player is retired
                        await Client.SetWinnerAsync(new GameResult
                        {
                            Winner = Second,
                            PlayerStarted = First,
                            Player2 = Second,
                            PointsPlayerStarted = pointsPlayer2 + Bonus(1),  // the points were given to the player who retired
                            PointsPlayer2 = 1000 + Bonus(2),  // winning points to other player
                            CellsFilled = cellsFilled
                        });
                    if (Username == Second)  // if the player who didn't start is retired
                        await Client.SetWinnerAsync(new GameResult
                        {
                            Winner = First,
                            PlayerStarted = First,
                            Player2 = Second,
                            PointsPlayerStarted = 1000 + Bonus(1),  // winning points to other player
                            PointsPlayer2 = pointsPlayerStarted + Bonus(2),  // the points were given to the player who retired
                            CellsFilled = cellsFilled
                        });
                    DisconnectGame();
                }
                catch (RpcException ex)
                {

                    if (ex.Message != new RpcException(new Status(StatusCode.NotFound, "Connection with the player is lost")).Message
                        && ex.Message != new RpcException(new Status(StatusCode.NotFound, "Your Connection in the game is lost")).Message)
                    {
                        MessageBox.Show("Connection with the server is lost"); // if it is unknown exception, the server probably crashed
                        return;
                    }
                    else
                        MessageBox.Show(ex.Message);  // display the specific message
                }
            }
        }
    }
}
