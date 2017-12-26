using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ExDevilLee.Collections
{
    /// <summary>
    /// Manage collections with expiration time.
    /// </summary>
    public class KVCacheManager<TKey, TValue>
    {
        private object locker = new object();
        private IDictionary<TKey, CacheItem> m_CacheDict;
        private Thread m_ThreadOfCheckExpiredItem;
        private int m_IntervalTimeOfCheckExpiredItemSeconds;
        private static readonly bool TKeyIsValueType = typeof(TKey).IsValueType;
        private static readonly bool TValueIsValueType = typeof(TValue).IsValueType;

        public class ItemExpiredEventArgs : EventArgs
        {
            public TKey Key { get; set; }
            public TValue Value { get; set; }
            public ItemExpiredEventArgs(TKey key, TValue value)
            {
                this.Key = key;
                this.Value = value;
            }
        }
        public event EventHandler<ItemExpiredEventArgs> ItemExpired;
        protected virtual void OnItemExpired(ItemExpiredEventArgs e)
        {
            EventHandler<ItemExpiredEventArgs> h = ItemExpired;
            if (null != h) h(this, e);
        }

        public KVCacheManager() : this(5) { }
        public KVCacheManager(int intervalTimeOfCheckExpiredItemSeconds)
        {
            m_IntervalTimeOfCheckExpiredItemSeconds = intervalTimeOfCheckExpiredItemSeconds;
            m_CacheDict = new Dictionary<TKey, CacheItem>();
            m_ThreadOfCheckExpiredItem = new Thread(this.CheckAndRemoveExpiredItem);
            m_ThreadOfCheckExpiredItem.IsBackground = true;
            m_ThreadOfCheckExpiredItem.Start();
        }
        private void CheckAndRemoveExpiredItem()
        {
            while (true)
            {
                KeyValuePair<TKey, CacheItem>[] tmpArray = null;
                if (null != m_CacheDict && m_CacheDict.Count > 0)
                {
                    lock (locker)
                    {
                        if (null != m_CacheDict && m_CacheDict.Count > 0)
                        {
                            tmpArray = new KeyValuePair<TKey, CacheItem>[m_CacheDict.Count];
                            m_CacheDict.CopyTo(tmpArray, 0);
                        }
                    }
                }

                List<CacheItem> expiredItemList = null;
                if (null != tmpArray && tmpArray.Length > 0)
                {
                    expiredItemList = new List<CacheItem>();
                    Parallel.ForEach(tmpArray, item =>
                    {
                        if (!TKeyIsValueType && null == item.Key) return;
                        if (item.Value.IsExpired) expiredItemList.Add(item.Value);
                    });
                }

                if (null != expiredItemList && expiredItemList.Count > 0)
                {
                    lock (locker)
                    {
                        Parallel.ForEach(expiredItemList, item =>
                        {
                            if (!TKeyIsValueType && null == item.Key) return;
                            if (!m_CacheDict.ContainsKey(item.Key)) return;
                            if (m_CacheDict[item.Key].IsExpired)
                            {
                                m_CacheDict.Remove(item.Key);
                                this.OnItemExpired(new ItemExpiredEventArgs(item.Key, item.Value));
                            }
                        });
                    }
                }

                Thread.Sleep(m_IntervalTimeOfCheckExpiredItemSeconds * 1000);
            }
        }

        ~KVCacheManager()
        {
            this.Clear();
            if (null != m_ThreadOfCheckExpiredItem && m_ThreadOfCheckExpiredItem.IsAlive)
            {
                try
                {
                    m_ThreadOfCheckExpiredItem.Abort();
                }
                catch { }
                finally
                {
                    m_ThreadOfCheckExpiredItem = null;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (locker)
                {
                    return m_CacheDict?.Count ?? 0;
                }
            }
        }

        public void AddItem(TKey key, TValue value, uint expirationOfSeconds)
        {
            if (!TKeyIsValueType && null == key) throw new ArgumentNullException(nameof(key));

            var item = new CacheItem(key, value, expirationOfSeconds);
            lock (locker)
            {
                if (null == m_CacheDict)
                    m_CacheDict = new Dictionary<TKey, CacheItem>();

                if (m_CacheDict.ContainsKey(key))
                    m_CacheDict[key] = item;
                else
                    m_CacheDict.Add(key, item);
            }
        }

        public void AddRangeItems(IEnumerable<KeyValuePair<TKey, TValue>> items, uint expirationOfSeconds)
        {
            if (null == items) throw new ArgumentNullException(nameof(items));

            List<CacheItem> listOfCacheItem = new List<CacheItem>();
            foreach (var item in items)
            {
                listOfCacheItem.Add(new CacheItem(item.Key, item.Value, expirationOfSeconds));
            }

            lock (locker)
            {
                if (null == m_CacheDict)
                    m_CacheDict = new Dictionary<TKey, CacheItem>();

                foreach (var item in listOfCacheItem)
                {
                    if (m_CacheDict.ContainsKey(item.Key))
                        m_CacheDict[item.Key] = item;
                    else
                        m_CacheDict.Add(item.Key, item);
                }
            }
        }

        public bool TryGetItem(TKey key, out TValue value)
        {
            if (!TKeyIsValueType && null == key) throw new ArgumentNullException(nameof(key));

            value = default(TValue);
            lock (locker)
            {
                if (null == m_CacheDict) return false;
                if (!m_CacheDict.TryGetValue(key, out CacheItem item)) return false;
                if (item.IsExpired) return false;
                value = item.Value;
                return true;
            }
        }

        public void RemoveItem(TKey key)
        {
            if (!TKeyIsValueType && null == key) throw new ArgumentNullException(nameof(key));

            if (m_CacheDict.ContainsKey(key))
            {
                lock (locker)
                {
                    if (m_CacheDict.ContainsKey(key)) m_CacheDict.Remove(key);
                }
            }
        }

        public void RemoveRangeItems(IEnumerable<TKey> keys)
        {
            if (null == keys) throw new ArgumentNullException(nameof(keys));

            lock (locker)
            {
                foreach (var key in keys)
                {
                    if (!TKeyIsValueType && null == key) continue;
                    if (m_CacheDict.ContainsKey(key)) m_CacheDict.Remove(key);
                }
            }
        }

        public void Clear()
        {
            lock (locker) m_CacheDict = null;
        }

        private class CacheItem
        {
            private Stopwatch m_Stopwatch = new Stopwatch();

            public TKey Key { get; private set; }

            private TValue m_Value;
            public TValue Value
            {
                get
                {
                    m_Stopwatch.Restart();
                    return m_Value;
                }
            }

            public uint ExpirationOfSeconds { get; private set; }

            public bool IsExpired
            {
                get
                {
                    return m_Stopwatch.Elapsed.TotalSeconds > ExpirationOfSeconds;
                }
            }

            public CacheItem(TKey key, TValue value, uint expirationOfSeconds)
            {
                if (!TKeyIsValueType && null == key) throw new ArgumentNullException(nameof(key));

                this.Key = key;
                m_Value = value;
                this.ExpirationOfSeconds = expirationOfSeconds;
                m_Stopwatch.Start();
            }

            public override string ToString()
            {
                return $"Key : {this.Key}, Value : {this.Value}, Expiration : {this.ExpirationOfSeconds} (s)";
            }
        }
    }
}