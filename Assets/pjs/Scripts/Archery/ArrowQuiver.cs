using System.Collections.Generic;

namespace Archery
{
    /// <summary>
    /// 순서가 있는 화살통. 앞(0번)이 다음에 사용할 화살이다.
    /// - 같은 프레임에 함께 회수된 화살(동시 회수)은 무작위 순서로 뒤에 붙는다.
    /// - 서로 다른 시점에 회수된 화살(순차 회수)은 회수된 순서대로 뒤에 붙는다.
    /// </summary>
    public class ArrowQuiver
    {
        private readonly List<ArcheryArrow> order = new List<ArcheryArrow>();
        private readonly System.Random rng;

        public ArrowQuiver(int seed)
        {
            rng = new System.Random(seed);
        }

        public int Count => order.Count;
        public IReadOnlyList<ArcheryArrow> Order => order;
        public ArcheryArrow Peek() => order.Count > 0 ? order[0] : null;
        public bool Contains(ArcheryArrow a) => order.Contains(a);

        /// <summary>전투 시작 시 초기 구성</summary>
        public void InitialAdd(ArcheryArrow a)
        {
            if (!order.Contains(a)) order.Add(a);
        }

        /// <summary>다음 화살을 꺼낸다. 꺼낸 화살은 화살통에서 즉시 제거된다.</summary>
        public ArcheryArrow TakeNext()
        {
            if (order.Count == 0) return null;
            var a = order[0];
            order.RemoveAt(0);
            return a;
        }

        /// <summary>
        /// 회수된 화살 묶음을 넣는다. 묶음이 2발 이상이면 '동시 회수'로 보고 무작위로 섞는다.
        /// 반환값: 실제로 추가된 순서
        /// </summary>
        public List<ArcheryArrow> AddRecoveredBatch(List<ArcheryArrow> batch)
        {
            var added = new List<ArcheryArrow>(batch.Count);
            for (int i = 0; i < batch.Count; i++)
            {
                if (batch[i] != null && !order.Contains(batch[i]) && !added.Contains(batch[i])) added.Add(batch[i]);
            }
            if (added.Count > 1)
            {
                // Fisher-Yates
                for (int i = added.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    var tmp = added[i];
                    added[i] = added[j];
                    added[j] = tmp;
                }
            }
            order.AddRange(added);
            return added;
        }

        public bool HasDuplicates()
        {
            var set = new HashSet<ArcheryArrow>();
            for (int i = 0; i < order.Count; i++)
            {
                if (!set.Add(order[i])) return true;
            }
            return false;
        }
    }
}
