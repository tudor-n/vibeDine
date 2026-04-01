from collections import Counter


def build_co_occurrence(order_history: list[list[str]]) -> dict[tuple[str, str], int]:
    counts: dict[tuple[str, str], int] = {}
    for order in order_history:
        unique = sorted(set(order))
        for i in range(len(unique)):
            for j in range(i + 1, len(unique)):
                pair = (unique[i], unique[j])
                counts[pair] = counts.get(pair, 0) + 1
    return counts


def generate_similarity_rules(
    order_history: list[list[str]],
    threshold: float = 0.1,
) -> list[dict]:
    co_occurrence = build_co_occurrence(order_history)
    item_counts = Counter(item for order in order_history for item in order)

    rules: list[dict] = []
    for (a, b), count in co_occurrence.items():
        jaccard = count / (item_counts[a] + item_counts[b] - count)
        if jaccard >= threshold:
            rules.append({
                "item_a_id": a,
                "item_b_id": b,
                "score": round(jaccard, 4),
            })

    rules.sort(key=lambda r: r["score"], reverse=True)
    return rules
