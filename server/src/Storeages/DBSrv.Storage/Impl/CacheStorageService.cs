using OpenMir2.Packets.ServerPackets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DBSrv.Storage.Impl
{
    public class CacheStorageService : ICacheStorage
    {
        private readonly ConcurrentDictionary<string, CharacterDataInfo> _cacheMap = new ConcurrentDictionary<string, CharacterDataInfo>(StringComparer.OrdinalIgnoreCase);

        public void Add(string sChrName, CharacterDataInfo humDataInfo)
        {
            _cacheMap.AddOrUpdate(sChrName, humDataInfo,
                (_, current) => humDataInfo.PersistenceRevision > current.PersistenceRevision
                    ? humDataInfo : current);
        }

        public CharacterDataInfo Get(string sChrName, out bool exist)
        {
            exist = false;
            if (_cacheMap.TryGetValue(sChrName, out CharacterDataInfo? humDataInfo))
            {
                exist = true;
                return humDataInfo;
            }
            return null;
        }

        public void Delete(string ChrName)
        {
            if (_cacheMap.TryRemove(ChrName, out CharacterDataInfo _))
            {

            }
        }

        public bool RemoveIfUnchanged(string chrName, CharacterDataInfo expected)
        {
            return ((ICollection<KeyValuePair<string, CharacterDataInfo>>)_cacheMap)
                .Remove(new KeyValuePair<string, CharacterDataInfo>(chrName, expected));
        }

        public IEnumerator<CharacterDataInfo> QueryCacheData()
        {
            return _cacheMap.Values.GetEnumerator();
        }
    }
}