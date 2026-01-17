namespace YUCP.Motion.Core
{
    /// <summary>
    /// Monotonic integer identifier for elements. No string keys, no boxing.
    /// </summary>
    public struct ElementId
    {
        private static int s_NextId = 1;
        
        private readonly int m_Id;
        
        private ElementId(int id)
        {
            m_Id = id;
        }
        
        /// <summary>
        /// Creates a new unique ElementId.
        /// </summary>
        public static ElementId Create()
        {
            return new ElementId(s_NextId++);
        }
        
        /// <summary>
        /// Gets the integer value of this ID.
        /// </summary>
        public int Value => m_Id;
        
        public override int GetHashCode() => m_Id.GetHashCode();
        
        public override bool Equals(object obj)
        {
            return obj is ElementId other && m_Id == other.m_Id;
        }
        
        public bool Equals(ElementId other) => m_Id == other.m_Id;
        
        public static bool operator ==(ElementId left, ElementId right) => left.m_Id == right.m_Id;
        
        public static bool operator !=(ElementId left, ElementId right) => left.m_Id != right.m_Id;
    }
}
