using System;
using System.Collections.Generic;

namespace StockDice.Core
{
    /// <summary>
    /// PCG32 난수 생성기. System.Random 은 런타임(.NET / Mono / IL2CPP)마다 구현이 다를 수 있어서
    /// 온라인 동기화, 리플레이, 시뮬레이터 재현을 위해 직접 구현한다. 상태(State, Inc)만 저장하면 그대로 복원된다.
    /// </summary>
    [Serializable]
    public sealed class Rng
    {
        public ulong State;
        public ulong Inc;

        public Rng(ulong seed, ulong sequence = 54)
        {
            State = 0;
            Inc = (sequence << 1) | 1UL;
            NextUInt();
            State += seed;
            NextUInt();
        }

        private Rng() { }

        public Rng Clone()
        {
            return new Rng { State = State, Inc = Inc };
        }

        public uint NextUInt()
        {
            unchecked
            {
                ulong old = State;
                State = old * 6364136223846793005UL + Inc;
                uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
            }
        }

        /// <summary>[min, max) 정수. 편향 없는 방식.</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint bound = (uint)(maxExclusive - minInclusive);
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return minInclusive + (int)(r % bound);
            }
        }

        /// <summary>[0, 1)</summary>
        public double NextDouble()
        {
            return NextUInt() / 4294967296.0;
        }

        public double Range(double minInclusive, double maxExclusive)
        {
            return minInclusive + (maxExclusive - minInclusive) * NextDouble();
        }

        public bool Chance(double probability)
        {
            return NextDouble() < probability;
        }

        public T Pick<T>(IList<T> list)
        {
            return list[Range(0, list.Count)];
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
