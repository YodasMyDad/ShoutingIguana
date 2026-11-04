// Text set in parts ("3" strong and "URLs" quieter, a severity's number and its word) reads as one string to people
// and screen readers, but Testing Library's text queries match one element's own text. This matches the innermost
// element whose whole text is the string.
export function wholeText(text: string) {
  return (_content: string, element: Element | null): boolean =>
    element !== null &&
    element.textContent === text &&
    Array.from(element.children).every((child) => child.textContent !== text);
}
