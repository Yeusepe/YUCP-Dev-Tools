using System;
using System.Collections.Generic;

namespace YUCP.Motion.Core
{
    /// <summary>
    /// Generic subscription manager for change listeners. Allocation-free, matches motion-main API.
    /// </summary>
    public class SubscriptionManager<THandler> where THandler : Delegate
    {
        private readonly List<THandler> m_Subscriptions = new List<THandler>();
        
        /// <summary>
        /// Number of active subscriptions.
        /// </summary>
        public int Count => m_Subscriptions.Count;
        
        /// <summary>
        /// Adds a subscription. Returns an unsubscribe function.
        /// </summary>
        public System.Func<bool> Add(THandler handler)
        {
            if (handler == null)
                return () => false;
            
            // Add unique item (motion-main behavior)
            if (!m_Subscriptions.Contains(handler))
            {
                m_Subscriptions.Add(handler);
            }
            
            // Return unsubscribe function
            return () => Remove(handler);
        }
        
        /// <summary>
        /// Removes a subscription. Returns true if removed.
        /// </summary>
        public bool Remove(THandler handler)
        {
            return m_Subscriptions.Remove(handler);
        }
        
        /// <summary>
        /// Notifies all subscribers. Supports up to 3 parameters.
        /// </summary>
        public void Notify<T1>(T1 a)
        {
            int numSubscriptions = m_Subscriptions.Count;
            if (numSubscriptions == 0) return;
            
            if (numSubscriptions == 1)
            {
                // Single handler optimization
                if (m_Subscriptions[0] is Action<T1> action)
                {
                    action(a);
                }
            }
            else
            {
                // Multiple handlers - check existence before firing (motion-main behavior)
                for (int i = 0; i < m_Subscriptions.Count; i++)
                {
                    if (i < m_Subscriptions.Count && m_Subscriptions[i] is Action<T1> action)
                    {
                        action(a);
                    }
                }
            }
        }
        
        /// <summary>
        /// Notifies all subscribers with 2 parameters.
        /// </summary>
        public void Notify<T1, T2>(T1 a, T2 b)
        {
            int numSubscriptions = m_Subscriptions.Count;
            if (numSubscriptions == 0) return;
            
            if (numSubscriptions == 1)
            {
                if (m_Subscriptions[0] is Action<T1, T2> action)
                {
                    action(a, b);
                }
            }
            else
            {
                for (int i = 0; i < m_Subscriptions.Count; i++)
                {
                    if (i < m_Subscriptions.Count && m_Subscriptions[i] is Action<T1, T2> action)
                    {
                        action(a, b);
                    }
                }
            }
        }
        
        /// <summary>
        /// Notifies all subscribers with 3 parameters.
        /// </summary>
        public void Notify<T1, T2, T3>(T1 a, T2 b, T3 c)
        {
            int numSubscriptions = m_Subscriptions.Count;
            if (numSubscriptions == 0) return;
            
            if (numSubscriptions == 1)
            {
                if (m_Subscriptions[0] is Action<T1, T2, T3> action)
                {
                    action(a, b, c);
                }
            }
            else
            {
                for (int i = 0; i < m_Subscriptions.Count; i++)
                {
                    if (i < m_Subscriptions.Count && m_Subscriptions[i] is Action<T1, T2, T3> action)
                    {
                        action(a, b, c);
                    }
                }
            }
        }
        
        /// <summary>
        /// Clears all subscriptions.
        /// </summary>
        public void Clear()
        {
            m_Subscriptions.Clear();
        }
    }
}
