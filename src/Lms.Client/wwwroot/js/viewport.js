// Tells .NET whether the viewport is a phone (under 600 px, L19: the gradebook renders ONE layout, not both hidden with CSS) and when it changes.
const query = window.matchMedia('(max-width: 599.98px)');
export function watch(ref) {
    query.addEventListener('change', e => ref.invokeMethodAsync('OnChanged', e.matches));
    return query.matches;
}
