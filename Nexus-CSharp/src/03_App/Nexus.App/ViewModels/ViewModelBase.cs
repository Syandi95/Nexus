using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Nexus.App.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_canExecute == null) return true;
            TryConvertParameter(parameter, out T? typedParam);
            return _canExecute(typedParam);
        }

        public void Execute(object? parameter)
        {
            if (TryConvertParameter(parameter, out T? typedParam))
            {
                _execute(typedParam);
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public static bool TryConvertParameter(object? parameter, out T? result)
        {
            if (parameter == null)
            {
                result = default;
                return true;
            }

            if (parameter is T directMatch)
            {
                result = directMatch;
                return true;
            }

            Type targetType = typeof(T);
            Type? underlyingType = Nullable.GetUnderlyingType(targetType);
            Type actualTargetType = underlyingType ?? targetType;

            try
            {
                if (actualTargetType.IsEnum)
                {
                    if (parameter is string strEnum)
                    {
                        result = (T)Enum.Parse(actualTargetType, strEnum, ignoreCase: true);
                        return true;
                    }
                    else if (parameter is IConvertible)
                    {
                        result = (T)Enum.ToObject(actualTargetType, parameter);
                        return true;
                    }
                }

                if (parameter is string strParam)
                {
                    if (actualTargetType == typeof(Guid) && Guid.TryParse(strParam, out Guid g))
                    {
                        result = (T)(object)g;
                        return true;
                    }

                    if (actualTargetType == typeof(bool) && bool.TryParse(strParam, out bool b))
                    {
                        result = (T)(object)b;
                        return true;
                    }
                }

                object? converted = Convert.ChangeType(parameter, actualTargetType, System.Globalization.CultureInfo.InvariantCulture);
                result = (T?)converted;
                return true;
            }
            catch
            {
                try
                {
                    var converter = TypeDescriptor.GetConverter(actualTargetType);
                    if (converter != null && converter.CanConvertFrom(parameter.GetType()))
                    {
                        result = (T?)converter.ConvertFrom(parameter);
                        return true;
                    }
                }
                catch
                {
                }

                result = default;
                return false;
            }
        }
    }

    public sealed class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _executeAsync;
        private readonly Predicate<T?>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(
            Func<T?, Task> executeAsync,
            Predicate<T?>? canExecute = null)
        {
            _executeAsync = executeAsync
                ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting ||
                !RelayCommand<T>.TryConvertParameter(parameter, out var value))
            {
                return false;
            }

            return _canExecute?.Invoke(value) ?? true;
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter) ||
                !RelayCommand<T>.TryConvertParameter(parameter, out var value))
            {
                return;
            }

            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _executeAsync(value);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
