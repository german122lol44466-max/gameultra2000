using System;
using System.Text;
using UnityEngine;

namespace SW.Commander.Net
{
    /// <summary>Типы сообщений протокола. Первый байт каждого пакета.</summary>
    public enum Msg : byte
    {
        // --- лобби
        Hello = 1,          // клиент -> сервер: версия, имя, SteamID
        Welcome,            // сервер -> клиент: ваш id
        Reject,             // сервер -> клиент: причина
        LobbyState,         // сервер -> все: игроки и настройки
        SetReady,           // клиент -> сервер
        SetTeam,            // клиент -> сервер
        Chat,               // в обе стороны
        Kick,               // сервер -> клиент
        LoadMatch,          // сервер -> все: загрузить бой (сид, настройки)
        Loaded,             // клиент -> сервер: сцена загружена
        BeginMatch,         // сервер -> все: бой начался
        ReturnToLobby,      // сервер -> все
        Ping,               // в обе стороны

        // --- бой: сервер -> клиенты
        Snapshot = 32,      // позиции юнитов (ненадёжно, частями)
        UnitSpawn,          // юнит появился (тип, команда, отряд)
        UnitDespawn,
        UnitDeath,          // смерть (направление, тип урона)
        Shots,              // выстрелы (визуал болтов)
        Explosion,
        SquadState,         // состав и приказы отрядов
        Objectives,         // командные пункты, генераторы, очки, тикеты
        StructureState,     // здоровье построек
        AvatarStates,       // командиры других игроков (ретрансляция)
        PlayerEconomy,      // снабжение игрока
        Voice,              // голосовая реплика юнита
        MatchEnd,
        Notice,             // текст на экран (захват пункта и т. п.)
        CommanderDamage,    // урон по командиру конкретного игрока
        Hits,               // попадания (искры)

        // --- бой: клиент -> сервер
        AvatarInput = 64,   // позиция/поворот командира (ненадёжно)
        Order,              // приказ отрядам
        SquadConfig,        // строй, стойка, походка, правила огня, имя
        Recruit,            // набрать бойцов
        SquadEdit,          // создать/распустить/перевести бойцов
        CommanderFire,      // выстрел командира
        CommanderGrenade,
        CommanderRespawn,
    }

    /// <summary>Запись пакета: компактные типы, квантование позиций и углов.</summary>
    public class NetWriter
    {
        byte[] buf;
        public int Length;

        public NetWriter(int capacity = 256) { buf = new byte[capacity]; }

        public byte[] Buffer => buf;
        public ArraySegment<byte> Segment => new ArraySegment<byte>(buf, 0, Length);

        public NetWriter Reset(Msg type)
        {
            Length = 0;
            Byte((byte)type);
            return this;
        }

        void Ensure(int n)
        {
            if (Length + n <= buf.Length) return;
            int size = buf.Length * 2;
            while (size < Length + n) size *= 2;
            Array.Resize(ref buf, size);
        }

        public NetWriter Byte(byte v) { Ensure(1); buf[Length++] = v; return this; }
        public NetWriter Bool(bool v) { Byte(v ? (byte)1 : (byte)0); return this; }
        public NetWriter SByte(sbyte v) { Byte((byte)v); return this; }

        public NetWriter UShort(ushort v)
        {
            Ensure(2);
            buf[Length++] = (byte)v;
            buf[Length++] = (byte)(v >> 8);
            return this;
        }

        public NetWriter Short(short v) { UShort((ushort)v); return this; }

        public NetWriter Int(int v)
        {
            Ensure(4);
            buf[Length++] = (byte)v;
            buf[Length++] = (byte)(v >> 8);
            buf[Length++] = (byte)(v >> 16);
            buf[Length++] = (byte)(v >> 24);
            return this;
        }

        public NetWriter UInt(uint v) { Int((int)v); return this; }

        public NetWriter ULong(ulong v)
        {
            Int((int)(v & 0xffffffff));
            Int((int)(v >> 32));
            return this;
        }

        public NetWriter Float(float v)
        {
            var bits = BitConverter.SingleToInt32Bits(v);
            Int(bits);
            return this;
        }

        public NetWriter String(string s)
        {
            s ??= "";
            var bytes = Encoding.UTF8.GetBytes(s);
            int n = Mathf.Min(bytes.Length, 1024);
            UShort((ushort)n);
            Ensure(n);
            System.Buffer.BlockCopy(bytes, 0, buf, Length, n);
            Length += n;
            return this;
        }

        public NetWriter Vector3(Vector3 v) { Float(v.x); Float(v.y); Float(v.z); return this; }

        /// <summary>Позиция на карте: 5 см, ±1638 м по каждой оси.</summary>
        public NetWriter Pos(Vector3 v)
        {
            Short(Q(v.x, 20f));
            Short(Q(v.y, 20f));
            Short(Q(v.z, 20f));
            return this;
        }

        /// <summary>Единичное направление: 3 x sbyte.</summary>
        public NetWriter Dir(Vector3 d)
        {
            d = d.sqrMagnitude > 1e-6f ? d.normalized : UnityEngine.Vector3.forward;
            SByte((sbyte)Mathf.RoundToInt(d.x * 127f));
            SByte((sbyte)Mathf.RoundToInt(d.y * 127f));
            SByte((sbyte)Mathf.RoundToInt(d.z * 127f));
            return this;
        }

        /// <summary>Угол 0..360 в байт.</summary>
        public NetWriter Angle(float deg) { Byte((byte)Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 360f * 256f)); return this; }

        /// <summary>Угол 0..360 в 2 байта (для своего командира — плавнее).</summary>
        public NetWriter Angle16(float deg) { UShort((ushort)Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 360f * 65535f)); return this; }

        static short Q(float v, float scale) => (short)Mathf.Clamp(Mathf.RoundToInt(v * scale), short.MinValue, short.MaxValue);
    }

    public class NetReader
    {
        byte[] buf;
        int pos, end;

        public NetReader() { }
        public NetReader(ArraySegment<byte> seg) { Set(seg); }

        public void Set(ArraySegment<byte> seg)
        {
            buf = seg.Array;
            pos = seg.Offset;
            end = seg.Offset + seg.Count;
        }

        public int Remaining => end - pos;
        public bool AtEnd => pos >= end;

        void Need(int n)
        {
            if (pos + n > end) throw new IndexOutOfRangeException("пакет короче ожидаемого");
        }

        public byte Byte() { Need(1); return buf[pos++]; }
        public bool Bool() => Byte() != 0;
        public sbyte SByte() => (sbyte)Byte();

        public ushort UShort()
        {
            Need(2);
            ushort v = (ushort)(buf[pos] | (buf[pos + 1] << 8));
            pos += 2;
            return v;
        }

        public short Short() => (short)UShort();

        public int Int()
        {
            Need(4);
            int v = buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16) | (buf[pos + 3] << 24);
            pos += 4;
            return v;
        }

        public uint UInt() => (uint)Int();

        public ulong ULong()
        {
            ulong lo = (uint)Int();
            ulong hi = (uint)Int();
            return lo | (hi << 32);
        }

        public float Float() => BitConverter.Int32BitsToSingle(Int());

        public string String()
        {
            int n = UShort();
            Need(n);
            var s = Encoding.UTF8.GetString(buf, pos, n);
            pos += n;
            return s;
        }

        public Vector3 Vector3() => new Vector3(Float(), Float(), Float());
        public Vector3 Pos() => new Vector3(Short() / 20f, Short() / 20f, Short() / 20f);
        public Vector3 Dir() => new Vector3(SByte() / 127f, SByte() / 127f, SByte() / 127f).normalized;
        public float Angle() => Byte() / 256f * 360f;
        public float Angle16() => UShort() / 65535f * 360f;
    }
}
