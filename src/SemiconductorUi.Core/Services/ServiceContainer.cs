using System;
using System.Collections.Generic;

namespace SemiconductorUi.Services
{
    /// <summary>
    /// 간단한 의존성 주입 컨테이너 (서비스 로케이터 패턴)
    /// WinForms 애플리케이션용 경량 구현
    /// </summary>
    public static class ServiceContainer
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private static readonly object _lock = new object();

        /// <summary>
        /// 서비스 등록
        /// </summary>
        public static void Register<T>(T instance) where T : class
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            lock (_lock)
            {
                _services[typeof(T)] = instance;
            }
        }

        /// <summary>
        /// 서비스 가져오기
        /// </summary>
        public static T Get<T>() where T : class
        {
            lock (_lock)
            {
                if (_services.TryGetValue(typeof(T), out var service))
                {
                    return service as T;
                }
            }

            return null;
        }

        /// <summary>
        /// 서비스 제거
        /// </summary>
        public static void Unregister<T>()
        {
            lock (_lock)
            {
                _services.Remove(typeof(T));
            }
        }

        /// <summary>
        /// 모든 서비스 초기화
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _services.Clear();
            }
        }

        /// <summary>
        /// 서비스가 등록되어 있는지 확인
        /// </summary>
        public static bool IsRegistered<T>()
        {
            lock (_lock)
            {
                return _services.ContainsKey(typeof(T));
            }
        }
    }
}

