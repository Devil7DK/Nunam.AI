/** The first message for each field of a validation error. */
export function fieldMessages(errors: Record<string, string[]>): Record<string, string> {
  return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [field, messages[0]]));
}
