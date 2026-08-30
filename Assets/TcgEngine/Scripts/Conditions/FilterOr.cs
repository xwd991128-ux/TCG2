using System.Collections.Generic;
using UnityEngine;
using TcgEngine.Gameplay;

namespace TcgEngine
{
    [CreateAssetMenu(fileName = "filter", menuName = "TcgEngine/Filter/Or", order = 7)]
    public class FilterOr : FilterData
    {
        public FilterData[] filters;

        public override List<Card> FilterTargets(Game data, AbilityData ability, Card caster, List<Card> source, List<Card> dest)
        {
            if (filters == null || filters.Length == 0)
                return source;

            HashSet<Card> validTargets = new HashSet<Card>();

            foreach (FilterData filter in filters)
            {
                if (filter != null)
                {
                    List<Card> filtered = filter.FilterTargets(data, ability, caster, source, dest);
                    foreach (Card card in filtered)
                    {
                        validTargets.Add(card);
                    }
                }
            }

            dest.Clear();
            dest.AddRange(validTargets);
            return dest;
        }
    }
}