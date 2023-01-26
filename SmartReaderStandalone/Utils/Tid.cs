using System.Collections;

namespace SmartReader.Infrastructure.Utils;

public class Tid : IEquatable<Tid>, IComparable<Tid>, IComparable, IEnumerable<ushort>, IEnumerable
{
    private readonly ushort[] _tidArray;

    public Tid(IEnumerable<ushort> tidEnumerable)
    {
        _tidArray = (tidEnumerable != null ? tidEnumerable.ToArray() : null) ??
                    throw new ArgumentNullException(nameof(tidEnumerable));
    }

    public Tid(IEnumerable<byte> tidEnumerable)
    {
        var numArray = (tidEnumerable != null ? tidEnumerable.ToArray() : null) ??
                       throw new ArgumentNullException(nameof(tidEnumerable));
        _tidArray = new ushort[numArray.Length / 2];
        for (var index = 0; index < numArray.Length; index += 2)
        {
            var num1 = numArray[index] << 8;
            int num2 = numArray[index + 1];
            _tidArray[index / 2] = (ushort) (num1 | num2);
        }
    }

    public ushort this[int index] => _tidArray[index];

    public int Count => _tidArray.Length;

    public string TIDClassSection { get; private set; }

    public string TIDManufacturerSection { get; private set; }

    public string TIDModelSection { get; private set; }

    public int CompareTo(object obj)
    {
        if (obj == null)
            return 1;
        return obj is Tid other ? CompareTo(other) : throw new ArgumentException("Object must be of type Tid");
    }

    public int CompareTo(Tid other)
    {
        if (other == null)
            return 1;
        var num1 = _tidArray.Length - other._tidArray.Length;
        if (num1 != 0)
            return num1;
        foreach (var data in _tidArray.Zip(other._tidArray, (b, b1) => new
                 {
                     T = b,
                     O = b1
                 }))
        {
            var num2 = data.T - data.O;
            if (num2 != 0)
                return num2;
        }

        return 0;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<ushort> GetEnumerator()
    {
        return ((IEnumerable<ushort>) _tidArray).GetEnumerator();
    }

    public bool Equals(Tid other)
    {
        return other is not null && _tidArray.SequenceEqual(other._tidArray);
    }

    public override bool Equals(object obj)
    {
        return obj != null && obj is Tid other && Equals(other);
    }

    public override int GetHashCode()
    {
        var num = _tidArray.Length;
        foreach (var tid in _tidArray)
            num = num * 397 + tid.GetHashCode();
        return num;
    }

    public static bool operator ==(Tid a, Tid b)
    {
        return Equals(a, b);
    }

    public static bool operator !=(Tid a, Tid b)
    {
        return !(a == b);
    }

    public static bool operator <(Tid left, Tid right)
    {
        return Comparer<Tid>.Default.Compare(left, right) < 0;
    }

    public static bool operator >(Tid left, Tid right)
    {
        return Comparer<Tid>.Default.Compare(left, right) > 0;
    }

    public static bool operator <=(Tid left, Tid right)
    {
        return Comparer<Tid>.Default.Compare(left, right) <= 0;
    }

    public static bool operator >=(Tid left, Tid right)
    {
        return Comparer<Tid>.Default.Compare(left, right) >= 0;
    }

    public override string ToString()
    {
        var chArray = new char[_tidArray.Length * 4];
        for (var index = 0; index < _tidArray.Length; ++index)
        {
            var num1 = _tidArray[index] >> 12;
            chArray[index * 4] = (char) (55 + num1 + (((num1 - 10) >> 31) & -7));
            var num2 = (_tidArray[index] >> 8) & 15;
            chArray[index * 4 + 1] = (char) (55 + num2 + (((num2 - 10) >> 31) & -7));
            var num3 = (_tidArray[index] >> 4) & 15;
            chArray[index * 4 + 2] = (char) (55 + num3 + (((num3 - 10) >> 31) & -7));
            var num4 = _tidArray[index] & 15;
            chArray[index * 4 + 3] = (char) (55 + num4 + (((num4 - 10) >> 31) & -7));
        }

        return new string(chArray);
    }

    public void ParseTID()
    {
        var str = ToString();
        if (str.Length >= 8)
        {
            TIDClassSection = str.Substring(0, 2);
            TIDManufacturerSection = str.Substring(2, 3);
            TIDModelSection = str.Substring(5, 3);
        }
        else
        {
            TIDClassSection = TIDManufacturerSection = TIDModelSection = string.Empty;
        }
    }
}