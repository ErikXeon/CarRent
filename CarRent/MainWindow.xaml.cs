using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
using System.Linq;

namespace CarRent
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer;
        private CarViewModel _selectedCar;
        private TripState _state;
        private TimeSpan _bookingLeft;
        private DateTime? _rideStart;
        private decimal _total;
        private int _pendingReviewStars;
        private RentalReviewContext _pendingReview;
        private int _nextRentalId;
        private string _selectedClassFilter;
        private double _maxPriceFilter;
        private double _minRatingFilter;

        public MainViewModel()
        {
            Cars = new ObservableCollection<CarViewModel>
            {
                new CarViewModel("Tesla Model 3", "Электро", 14, 96, "2 мин пешком · ул. Победы, 8", 4.9, 120, "tesla_model_3.jpg"),
                new CarViewModel("Volkswagen Polo", "Комфорт", 9, 62, "5 мин пешком · пр-т Мира, 15", 4.7, 88, "volkswagen_polo.jpg"),
                new CarViewModel("Haval Jolion", "Кроссовер", 12, 74, "3 мин пешком · ТЦ Центральный", 4.8, 57, "haval_jolion.jpg"),
                new CarViewModel("Kia Rio X", "Город", 8, 58, "6 мин пешком · ул. Ленина, 42", 4.6, 102, "kia_rio_x.jpg"),
                new CarViewModel("BMW i3", "Премиум", 17, 88, "4 мин пешком · Набережная", 4.9, 41, "bmw_i3.jpg"),
                new CarViewModel("Geely Coolray", "Спорт", 11, 67, "7 мин пешком · ул. Гагарина, 2", 4.5, 64, "geely_coolray.jpg"),
                new CarViewModel("Hyundai Solaris", "Город", 8, 71, "5 мин пешком · БЦ Альфа", 4.6, 95, "hyundai_solaris.jpg"),
                new CarViewModel("Nissan Qashqai", "Кроссовер", 12, 65, "8 мин пешком · ул. Советская, 4", 4.7, 53, "nissan_qashqai.jpg"),
                new CarViewModel("Audi A3", "Премиум", 16, 81, "4 мин пешком · Парк Сити", 4.8, 37, "audi_a3.jpg"),
                new CarViewModel("Renault Kaptur", "Семейный", 10, 69, "6 мин пешком · ул. Молодежная, 17", 4.5, 72, "renault_kaptur.jpg"),
                new CarViewModel("Exeed LX", "Бизнес", 15, 76, "9 мин пешком · ЖК Панорама", 4.7, 48, "exeed_lx.jpg"),
                new CarViewModel("Chery Tiggo 7", "Кроссовер", 11, 73, "3 мин пешком · ул. Университетская", 4.6, 60, "chery_tiggo_7.jpg")
            };

            FilteredCars = CollectionViewSource.GetDefaultView(Cars);
            FilteredCars.Filter = FilterCars;

            AvailableClasses = new ObservableCollection<string> { "Все классы" };
            foreach (var classTag in Cars.Select(car => car.ClassTag).Distinct().OrderBy(tag => tag))
            {
                AvailableClasses.Add(classTag);
            }

            MaxPriceLimit = Cars.Max(car => (double)car.PricePerMinute);
            _selectedClassFilter = "Все классы";
            _maxPriceFilter = MaxPriceLimit;
            _minRatingFilter = 0;

            SelectCarCommand = new RelayCommand(SelectCar);
            ReserveCommand = new RelayCommand(_ => Reserve());
            StartEngineCommand = new RelayCommand(_ => StartEngine());
            StartRideCommand = new RelayCommand(_ => StartRide());
            FinishAndPayCommand = new RelayCommand(_ => FinishAndPay());
            SetReviewStarsCommand = new RelayCommand(SetReviewStars);
            SubmitReviewCommand = new RelayCommand(_ => SubmitReview());
            ResetFiltersCommand = new RelayCommand(_ => ResetFilters());

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => OnTick();

            _state = TripState.NoSelection;
            _pendingReviewStars = 5;
            _nextRentalId = 1;
            ReceiptText = "Выбери машину, чтобы начать аренду.";
            RaiseAll();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<CarViewModel> Cars { get; }
        public ICollectionView FilteredCars { get; }
        public ObservableCollection<string> AvailableClasses { get; }

        public ICommand SelectCarCommand { get; }
        public ICommand ReserveCommand { get; }
        public ICommand StartEngineCommand { get; }
        public ICommand StartRideCommand { get; }
        public ICommand FinishAndPayCommand { get; }
        public ICommand SetReviewStarsCommand { get; }
        public ICommand SubmitReviewCommand { get; }
        public ICommand ResetFiltersCommand { get; }

        public double MaxPriceLimit { get; }

        public string SelectedClassFilter
        {
            get => _selectedClassFilter;
            set
            {
                if (_selectedClassFilter == value)
                {
                    return;
                }

                _selectedClassFilter = value;
                OnPropertyChanged(nameof(SelectedClassFilter));
                RefreshFilters();
            }
        }

        public double MaxPriceFilter
        {
            get => _maxPriceFilter;
            set
            {
                var bounded = Math.Max(0, Math.Min(MaxPriceLimit, value));
                if (_maxPriceFilter == bounded)
                {
                    return;
                }

                _maxPriceFilter = bounded;
                OnPropertyChanged(nameof(MaxPriceFilter));
                OnPropertyChanged(nameof(MaxPriceFilterText));
                RefreshFilters();
            }
        }

        public double MinRatingFilter
        {
            get => _minRatingFilter;
            set
            {
                var bounded = Math.Max(0, Math.Min(5, Math.Round(value, 1)));
                if (Math.Abs(_minRatingFilter - bounded) < 0.001)
                {
                    return;
                }

                _minRatingFilter = bounded;
                OnPropertyChanged(nameof(MinRatingFilter));
                OnPropertyChanged(nameof(MinRatingFilterText));
                RefreshFilters();
            }
        }

        public string MaxPriceFilterText => $"до {(int)MaxPriceFilter} ₽/мин";

        public string MinRatingFilterText => $"от {MinRatingFilter:0.0}★";

        public string FiltersSummary => $"Класс: {SelectedClassFilter}, цена: {MaxPriceFilterText}, рейтинг: {MinRatingFilterText}";

        public string SelectedCarName => _selectedCar == null
            ? "Машина не выбрана"
            : $"Выбрано: {_selectedCar.Name} · {_selectedCar.PricePerMinute} ₽/мин";

        public string HeaderStatus => _state == TripState.NoSelection ? "Ожидание выбора" : StageText;

        public string StageText
        {
            get
            {
                switch (_state)
                {
                    case TripState.NoSelection:
                        return "Выберите авто";
                    case TripState.Ready:
                        return "Готово к старту";
                    case TripState.Booked:
                        return "Бронь активна";
                    case TripState.EngineStarted:
                        return "Двигатель запущен";
                    case TripState.Riding:
                        return "Идет аренда";
                    case TripState.Finished:
                        return "Поездка завершена";
                    default:
                        return "Состояние неизвестно";
                }
            }
        }

        public string TimerText
        {
            get
            {
                if (_state == TripState.Booked || _state == TripState.EngineStarted)
                {
                    return $"До конца брони: {_bookingLeft:mm\\:ss}";
                }

                if (_state == TripState.Riding && _rideStart.HasValue)
                {
                    return $"Длительность аренды: {(DateTime.Now - _rideStart.Value):hh\\:mm\\:ss}";
                }

                return "Таймер не активен";
            }
        }

        public string RunningTotalText => _selectedCar == null ? "0 ₽" : $"{Math.Round(_total, 0)} ₽";

        public string BillingHint => _selectedCar == null
            ? "Сначала выбери автомобиль."
            : $"Тариф {_selectedCar.PricePerMinute} ₽/мин.";

        public string PendingReviewText => $"Выбрано: {_pendingReviewStars} из 5 звезд";

        public string ReviewHint => _pendingReview == null
            ? "Отзыв доступен после завершения аренды выбранной машины."
            : _pendingReview.IsSubmitted
                ? "За последнюю завершенную аренду отзыв уже отправлен."
                : $"Доступен отзыв за аренду №{_pendingReview.RentalId}: {_pendingReview.Car.Name}.";

        public string SelectedCarImagePath => _selectedCar?.ImagePath;

        public string SelectedCarImageStatusText => _selectedCar == null
            ? "Выберите машину, чтобы увидеть фото."
                : _selectedCar.HasImageFile
                ? ""
                : $"Фото не найдено: {_selectedCar.ImageRelativePath}. Проверьте копирование файлов в bin.";
        public string ReceiptText { get; private set; }

        private bool HasActiveTrip => _state == TripState.Booked || _state == TripState.EngineStarted || _state == TripState.Riding;

        private bool FilterCars(object item)
        {
            if (!(item is CarViewModel car))
            {
                return false;
            }

            var classMatches = SelectedClassFilter == "Все классы" || car.ClassTag == SelectedClassFilter;
            var priceMatches = (double)car.PricePerMinute <= MaxPriceFilter;
            var ratingMatches = car.Rating >= MinRatingFilter;
            return classMatches && priceMatches && ratingMatches;
        }

        private void RefreshFilters()
        {
            FilteredCars.Refresh();
            OnPropertyChanged(nameof(FiltersSummary));
        }

        private void ResetFilters()
        {
            _selectedClassFilter = "Все классы";
            _maxPriceFilter = MaxPriceLimit;
            _minRatingFilter = 0;
            OnPropertyChanged(nameof(SelectedClassFilter));
            OnPropertyChanged(nameof(MaxPriceFilter));
            OnPropertyChanged(nameof(MinRatingFilter));
            OnPropertyChanged(nameof(MaxPriceFilterText));
            OnPropertyChanged(nameof(MinRatingFilterText));
            RefreshFilters();
        }

        private void SelectCar(object parameter)
        {
            if (!(parameter is CarViewModel car))
            {
                return;
            }

            if (HasActiveTrip)
            {
                ReceiptText = "Сначала заверши текущую поездку, потом можно выбрать другую машину.";
                RaiseAll();
                return;
            }

            _selectedCar = car;
            _timer.Stop();
            _state = TripState.Ready;
            _bookingLeft = TimeSpan.Zero;
            _rideStart = null;
            _total = 0;
            _pendingReviewStars = 5;
            ReceiptText = $"{car.Name} выбран. Можно сразу начать аренду или сначала забронировать на 15 минут.";
            RaiseAll();
        }

        private void Reserve()
        {
            if (_selectedCar == null)
            {
                ReceiptText = "Сначала выберите машину для бронирования.";
                RaiseAll();
                return;
            }

            if (HasActiveTrip && _state != TripState.Ready)
            {
                ReceiptText = "Другая машина уже забронирована или в аренде. Сначала завершите текущую поездку.";
                RaiseAll();
                return;
            }

            _bookingLeft = TimeSpan.FromMinutes(15);
            _state = TripState.Booked;
            ReceiptText = "Бронь оформлена на 15 минут.";
            _timer.Start();
            RaiseAll();
        }

        private void StartEngine()
        {
            if (_selectedCar == null || _state == TripState.NoSelection)
            {
                ReceiptText = "Сначала выберите машину.";
                RaiseAll();
                return;
            }

            if (_state == TripState.Ready || _state == TripState.Finished)
            {
                ReceiptText = "Машина не арендована.";
                RaiseAll();
                return;
            }

            if (_state == TripState.Riding)
            {
                ReceiptText = "Двигатель уже работает: аренда уже начата.";
                RaiseAll();
                return;
            }

            _state = TripState.EngineStarted;
            ReceiptText = "Двигатель успешно запущен.";
            RaiseAll();
        }

        private void StartRide()
        {
            if (_selectedCar == null)
            {
                ReceiptText = "Сначала выберите машину.";
                RaiseAll();
                return;
            }

            if (_state == TripState.Riding)
            {
                ReceiptText = "Аренда уже идет.";
                RaiseAll();
                return;
            }

            _state = TripState.Riding;
            _pendingReview = null;
            _rideStart = DateTime.Now;
            _total = 0;
            _timer.Start();
            ReceiptText = "Аренда началась.";
            RaiseAll();
        }

        private void FinishAndPay()
        {
            if (_selectedCar == null)
            {
                ReceiptText = "Сначала выберите машину.";
                RaiseAll();
                return;
            }

            if (_state != TripState.Riding || !_rideStart.HasValue)
            {
                ReceiptText = "Сейчас нечего завершать и оплачивать: аренда не запущена.";
                RaiseAll();
                return;
            }

            _timer.Stop();
            var duration = DateTime.Now - _rideStart.Value;
            var minutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
            _total = minutes * _selectedCar.PricePerMinute;
            _state = TripState.Finished;
            _pendingReview = new RentalReviewContext(_nextRentalId++, _selectedCar);

            ReceiptText =
                "Счет сформирован и оплачен:\n" +
                $"• Автомобиль: {_selectedCar.Name}\n" +
                $"• Длительность: {duration:hh\\:mm\\:ss} ({minutes} мин)\n" +
                $"• Тариф: {_selectedCar.PricePerMinute} ₽/мин\n" +
                $"• Итого: {_total} ₽\n\n" +
                "Поездка закрыта. Теперь можно оставить 1 отзыв за эту аренду.";

            RaiseAll();
        }

        private void SetReviewStars(object parameter)
        {
            if (parameter == null)
            {
                return;
            }

            if (!int.TryParse(parameter.ToString(), out var stars))
            {
                return;
            }

            _pendingReviewStars = Math.Max(1, Math.Min(5, stars));
            RaiseAll();
        }

        private void SubmitReview()
        {
            if (_selectedCar == null)
            {
                ReceiptText = "Сначала выберите машину, чтобы оставить отзыв.";
                RaiseAll();
                return;
            }

            if (_pendingReview == null)
            {
                ReceiptText = "Отзыв можно оставить только после завершенной аренды.";
                RaiseAll();
                return;
            }

            if (_pendingReview.IsSubmitted)
            {
                ReceiptText = "За эту аренду отзыв уже отправлен. Новый отзыв будет доступен после следующей завершенной аренды.";
                RaiseAll();
                return;
            }

            if (!ReferenceEquals(_pendingReview.Car, _selectedCar))
            {
                ReceiptText = $"Отзыв можно оставить только для машины из завершенной аренды: {_pendingReview.Car.Name}.";
                RaiseAll();
                return;
            }

            if (_state != TripState.Finished)
            {
                ReceiptText = "Отзыв доступен только после завершения аренды.";
                RaiseAll();
                return;
            }


            _selectedCar.AddReview(_pendingReviewStars);
            _pendingReview.MarkSubmitted();
            ReceiptText = $"Отзыв за аренду №{_pendingReview.RentalId} отправлен: {_pendingReviewStars}★. Новый рейтинг {_selectedCar.RatingText}."; RaiseAll();
        }

        private void OnTick()
        {
            if ((_state == TripState.Booked || _state == TripState.EngineStarted) && _bookingLeft > TimeSpan.Zero)
            {
                _bookingLeft = _bookingLeft.Subtract(TimeSpan.FromSeconds(1));
                if (_bookingLeft <= TimeSpan.Zero)
                {
                    _state = TripState.Ready;
                    _bookingLeft = TimeSpan.Zero;
                    ReceiptText = "Время брони истекло. Можно забронировать снова или начать аренду.";
                }
            }

            if (_state == TripState.Riding && _rideStart.HasValue && _selectedCar != null)
            {
                var minutes = Math.Max(1, Math.Ceiling((DateTime.Now - _rideStart.Value).TotalMinutes));
                _total = (decimal)minutes * _selectedCar.PricePerMinute;
            }

            RaiseAll();
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(SelectedCarName));
            OnPropertyChanged(nameof(StageText));
            OnPropertyChanged(nameof(TimerText));
            OnPropertyChanged(nameof(RunningTotalText));
            OnPropertyChanged(nameof(BillingHint));
            OnPropertyChanged(nameof(PendingReviewText));
            OnPropertyChanged(nameof(ReviewHint));
            OnPropertyChanged(nameof(SelectedCarImagePath));
            OnPropertyChanged(nameof(SelectedCarImageStatusText));
            OnPropertyChanged(nameof(ReceiptText));
            OnPropertyChanged(nameof(HeaderStatus));
            OnPropertyChanged(nameof(FiltersSummary));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CarViewModel : INotifyPropertyChanged
    {
        private double _rating;
        private int _reviewsCount;

        public CarViewModel(string name, string classTag, decimal pricePerMinute, int batteryPercent, string location, double rating, int reviewsCount, string imageFileName)
        {
            Name = name;
            ClassTag = classTag;
            PricePerMinute = pricePerMinute;
            BatteryPercent = batteryPercent;
            Location = location;
            _rating = rating;
            _reviewsCount = reviewsCount;
            ImageFileName = imageFileName;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; }
        public string ClassTag { get; }
        public decimal PricePerMinute { get; }
        public int BatteryPercent { get; }
        public string Location { get; }
        public string ImageFileName { get; }
        public string ImageRelativePath => Path.Combine("Assets", "CarPhotos", ImageFileName);
        public string ImagePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ImageRelativePath);
        public bool HasImageFile => File.Exists(ImagePath);

        public double Rating => _rating;
        public int ReviewsCount => _reviewsCount;

        public string PricePerMinuteText => $"{PricePerMinute} ₽";
        public string BatteryText => $"Батарея / топливо: {BatteryPercent}%";
        public string RatingText => _rating.ToString("0.0");
        public string ReviewsCountText => $"Отзывы: {_reviewsCount}";

        public void AddReview(int stars)
        {
            var boundedStars = Math.Max(1, Math.Min(5, stars));
            _rating = ((_rating * _reviewsCount) + boundedStars) / (_reviewsCount + 1);
            _reviewsCount += 1;
            OnPropertyChanged(nameof(Rating));
            OnPropertyChanged(nameof(ReviewsCount));
            OnPropertyChanged(nameof(RatingText));
            OnPropertyChanged(nameof(ReviewsCountText));
        }

        public class RentalReviewContext
        {
            public RentalReviewContext(int rentalId, CarViewModel car)
            {
                RentalId = rentalId;
                Car = car;
            }

            public int RentalId { get; }
            public CarViewModel Car { get; }
            public bool IsSubmitted { get; private set; }

            public void MarkSubmitted()
            {
                IsSubmitted = true;
            }
        }


        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RentalReviewContext
    {
        public RentalReviewContext(int rentalId, CarViewModel car)
        {
            RentalId = rentalId;
            Car = car;
        }

        public int RentalId { get; }
        public CarViewModel Car { get; }
        public bool IsSubmitted { get; private set; }

        public void MarkSubmitted()
        {
            IsSubmitted = true;
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }

    public enum TripState
    {
        NoSelection,
        Ready,
        Booked,
        EngineStarted,
        Riding,
        Finished
    }
}
