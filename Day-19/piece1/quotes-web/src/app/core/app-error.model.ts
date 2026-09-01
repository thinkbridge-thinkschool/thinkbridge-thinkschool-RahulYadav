export interface ValidationErrors {
  [field: string]: string[];
}

// Typed application-level error surfaced to components in place of a raw
// HttpErrorResponse. See app-error.mapper.ts for how this is built from the
// real QuotesApi response shapes.
export interface AppError {
  status: number;
  message: string;
  detail?: string;
  validationErrors?: ValidationErrors;
}
