using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SeaBattle.Shared.Models;

namespace SeaBattle.Client
{
    public partial class GameBoardControl : UserControl, INotifyPropertyChanged
    {
        // Dependency Properties
        public static readonly DependencyProperty IsEnemyBoardProperty =
            DependencyProperty.Register("IsEnemyBoard", typeof(bool), typeof(GameBoardControl),
                new PropertyMetadata(false, OnBoardPropertyChanged));

        public static readonly DependencyProperty GameBoardProperty =
            DependencyProperty.Register("GameBoard", typeof(GameBoard), typeof(GameBoardControl),
                new PropertyMetadata(null, OnBoardPropertyChanged));

        public static readonly DependencyProperty ShowShipsProperty =
            DependencyProperty.Register("ShowShips", typeof(bool), typeof(GameBoardControl),
                new PropertyMetadata(true, OnBoardPropertyChanged));

        // Исправлено: добавлено ключевое слово new для скрытия свойства базового класса
        public new static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.Register("IsEnabled", typeof(bool), typeof(GameBoardControl),
                new PropertyMetadata(true, OnIsEnabledChanged));

        // Properties
        public bool IsEnemyBoard
        {
            get { return (bool)GetValue(IsEnemyBoardProperty); }
            set { SetValue(IsEnemyBoardProperty, value); }
        }

        public GameBoard GameBoard
        {
            get { return (GameBoard)GetValue(GameBoardProperty); }
            set { SetValue(GameBoardProperty, value); }
        }

        public bool ShowShips
        {
            get { return (bool)GetValue(ShowShipsProperty); }
            set { SetValue(ShowShipsProperty, value); }
        }

        // Исправлено: добавлено ключевое слово new для скрытия свойства базового класса
        public new bool IsEnabled
        {
            get { return (bool)GetValue(IsEnabledProperty); }
            set { SetValue(IsEnabledProperty, value); }
        }

        // Collections
        public ObservableCollection<ObservableCollection<CellViewModel>> Cells { get; set; }

        // Events
        public event EventHandler<CellClickEventArgs> CellClicked;
        public event PropertyChangedEventHandler PropertyChanged;

        // Board coordinates
        private List<string> _columns = new List<string> { "А", "Б", "В", "Г", "Д", "Е", "Ж", "З", "И", "К" };
        private List<string> _rows = new List<string> { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };

        public List<string> Columns { get { return _columns; } }
        public List<string> Rows { get { return _rows; } }

        // Board opacity for disabled state
        private double _boardOpacity = 1.0;
        public double BoardOpacity
        {
            get { return _boardOpacity; }
            set
            {
                _boardOpacity = value;
                OnPropertyChanged(nameof(BoardOpacity));
            }
        }

        public GameBoardControl()
        {
            InitializeComponent();

            Cells = new ObservableCollection<ObservableCollection<CellViewModel>>();
            DataContext = this;

            InitializeEmptyBoard();
        }

        private void InitializeEmptyBoard()
        {
            Cells.Clear();

            for (int y = 0; y < 10; y++)
            {
                var row = new ObservableCollection<CellViewModel>();
                for (int x = 0; x < 10; x++)
                {
                    row.Add(new CellViewModel
                    {
                        X = x,
                        Y = y,
                        State = CellState.Empty
                    });
                }
                Cells.Add(row);
            }
        }

        private static void OnBoardPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as GameBoardControl;
            control?.UpdateBoard();
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as GameBoardControl;
            if (control != null)
            {
                control.BoardOpacity = control.IsEnabled ? 1.0 : 0.6;
            }
        }

        public void UpdateBoard()
        {
            if (GameBoard == null) return;

            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    var cell = Cells[y][x];
                    var state = GameBoard.Cells[x, y];

                    if (IsEnemyBoard)
                    {
                        // Для вражеского поля показываем только видимые клетки
                        cell.State = GameBoard.VisibleCells[x, y] ? state : CellState.Empty;
                    }
                    else
                    {
                        // Для своего поля показываем корабли только если ShowShips = true
                        cell.State = (ShowShips || state != CellState.Ship) ? state : CellState.Empty;
                    }
                }
            }
        }

        public void ResetBoard()
        {
            InitializeEmptyBoard();
            UpdateBoard();
        }

        private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEnabled) return;

            var border = sender as Border;
            if (border?.DataContext is CellViewModel cell)
            {
                CellClicked?.Invoke(this, new CellClickEventArgs(cell.X, cell.Y));
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class CellViewModel : INotifyPropertyChanged
    {
        private int _x;
        private int _y;
        private CellState _state;

        public int X
        {
            get { return _x; }
            set
            {
                _x = value;
                OnPropertyChanged(nameof(X));
            }
        }

        public int Y
        {
            get { return _y; }
            set
            {
                _y = value;
                OnPropertyChanged(nameof(Y));
            }
        }

        public CellState State
        {
            get { return _state; }
            set
            {
                _state = value;
                OnPropertyChanged(nameof(State));
                OnPropertyChanged(nameof(Background));
                OnPropertyChanged(nameof(BorderBrush));
                OnPropertyChanged(nameof(IsHit));
            }
        }

        public Brush Background
        {
            get
            {
                if (_state == CellState.Ship) return new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Зеленый
                if (_state == CellState.Hit) return new SolidColorBrush(Color.FromRgb(231, 76, 60));   // Красный
                if (_state == CellState.Destroyed) return new SolidColorBrush(Color.FromRgb(192, 57, 43)); // Темно-красный
                if (_state == CellState.Miss) return new SolidColorBrush(Color.FromRgb(52, 73, 94));   // Синий
                return new SolidColorBrush(Color.FromRgb(45, 45, 48)); // Темно-серый
            }
        }

        public Brush BorderBrush
        {
            get
            {
                if (_state == CellState.Destroyed) return new SolidColorBrush(Color.FromRgb(255, 255, 255));
                return new SolidColorBrush(Color.FromRgb(85, 85, 85));
            }
        }

        public bool IsHit
        {
            get { return _state == CellState.Hit || _state == CellState.Destroyed || _state == CellState.Miss; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class CellClickEventArgs : EventArgs
    {
        public int X { get; set; }
        public int Y { get; set; }

        public CellClickEventArgs(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}