using Microsoft.Graph;
using System;
using System.Diagnostics.CodeAnalysis;

namespace ChatLifecycle.Models
{
    public class ChatItem : IComparable<ChatItem>
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string URL { get; set; }

        public string FormatedLastUpdated { get; set; }

        public DateTimeOffset LastUpdated { get; set; }

        public int CompareTo([AllowNull] ChatItem other)
        {            
            return DateTimeOffset.Compare(other.LastUpdated, this.LastUpdated);
        }
    }
}
