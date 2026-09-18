using System.Text;

namespace TypeSense.Services;

public sealed class InputBufferService
{
    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new();
    private readonly int _maxLength;

    public InputBufferService(int maxLength = 30)
    {
        _maxLength = Math.Max(8, maxLength);
    }

    public string Append(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return GetBuffer();
        }

        lock (_gate)
        {
            _buffer.Append(text);
            TrimToMaxLength();
            return _buffer.ToString();
        }
    }

    public string Backspace()
    {
        lock (_gate)
        {
            if (_buffer.Length == 0)
            {
                return string.Empty;
            }

            var removeLength = 1;
            if (_buffer.Length >= 2 && char.IsLowSurrogate(_buffer[^1]) && char.IsHighSurrogate(_buffer[^2]))
            {
                removeLength = 2;
            }

            _buffer.Remove(_buffer.Length - removeLength, removeLength);
            return _buffer.ToString();
        }
    }

    public void ReplaceFromTextBeforeCaret(string textBeforeCaret)
    {
        lock (_gate)
        {
            _buffer.Clear();
            if (!string.IsNullOrEmpty(textBeforeCaret))
            {
                _buffer.Append(textBeforeCaret);
                TrimToMaxLength();
            }
        }
    }

    public string GetCurrentToken()
    {
        lock (_gate)
        {
            var value = _buffer.ToString();
            var start = value.Length;

            while (start > 0 && !IsTokenBoundary(value[start - 1]))
            {
                start--;
            }

            return value[start..];
        }
    }

    public string GetBuffer()
    {
        lock (_gate)
        {
            return _buffer.ToString();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    private void TrimToMaxLength()
    {
        if (_buffer.Length <= _maxLength)
        {
            return;
        }

        _buffer.Remove(0, _buffer.Length - _maxLength);
    }

    private static bool IsTokenBoundary(char value)
    {
        return char.IsWhiteSpace(value) || !char.IsLetterOrDigit(value) && value != '_';
    }
}
