import { Component, inject, output, signal } from '@angular/core';
import { form, validate, requiredError, maxLengthError, FormField, FormRoot } from '@angular/forms/signals';
import type { FieldState } from '@angular/forms/signals';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { QuoteService } from '../core/quote.service';
import type { Quote } from '../core/quote.model';

// Matches QuotesApi.Models.Quote's constants exactly (see Quote.Create):
// IsNullOrWhiteSpace -> required, then trimmed length must fall in range.
const AUTHOR_MAX = 200; // Quote.MaxAuthorLength
const TEXT_MAX = 1000; // Quote.MaxTextLength

interface QuoteCreateModel {
  author: string;
  text: string;
}

// A single validator mirrors the backend's IsNullOrWhiteSpace + trimmed-length
// check. Signal Forms' built-in `required()`/`maxLength()` validate the raw
// (untrimmed) value, so they can't express "whitespace-only counts as empty"
// on their own - a custom `validate()` is still required, same as with
// Reactive Forms.
function quoteFieldError(rawValue: string, max: number) {
  const trimmed = rawValue.trim();
  if (trimmed.length === 0) {
    return requiredError();
  }
  if (trimmed.length > max) {
    return maxLengthError(max);
  }
  return undefined;
}

@Component({
  selector: 'app-quote-create',
  imports: [FormField, FormRoot],
  templateUrl: './quote-create.html',
  styleUrl: './quote-create.css',
})
export class QuoteCreate {
  private readonly quoteService = inject(QuoteService);

  // Emitted with the created quote so the parent can show it, matching the
  // existing selectedId-in-parent pattern used by QuoteList's `select` output.
  readonly created = output<Quote>();

  private readonly model = signal<QuoteCreateModel>({ author: '', text: '' });

  readonly quoteForm = form(
    this.model,
    (p) => {
      validate(p.author, ({ value }) => quoteFieldError(value(), AUTHOR_MAX));
      validate(p.text, ({ value }) => quoteFieldError(value(), TEXT_MAX));
    },
    {
      submission: {
        action: async (field) => {
          const value = field().value();
          let quote: Quote;
          try {
            quote = await firstValueFrom(
              this.quoteService.createQuote({
                author: value.author.trim(),
                text: value.text.trim(),
              }),
            );
          } catch (err) {
            return { kind: 'server', message: this.messageForError(err as HttpErrorResponse) };
          }
          this.quoteForm().reset({ author: '', text: '' });
          this.created.emit(quote);
          return undefined;
        },
        onInvalid: () => {
          this.focusFirstInvalid();
        },
      },
    },
  );

  authorErrorMessage(): string | null {
    return this.fieldErrorMessage(this.quoteForm.author(), 'Author', AUTHOR_MAX);
  }

  textErrorMessage(): string | null {
    return this.fieldErrorMessage(this.quoteForm.text(), 'Text', TEXT_MAX);
  }

  submitErrorMessage(): string | null {
    return this.quoteForm().errors().find((e) => e.kind === 'server')?.message ?? null;
  }

  private fieldErrorMessage(field: FieldState<string>, label: string, max: number): string | null {
    const errors = field.errors();
    if (errors.length === 0) {
      return null;
    }

    if (errors.some((e) => e.kind === 'required')) {
      return `${label} is required.`;
    }

    return `${label} must be 1-${max} characters.`;
  }

  private focusFirstInvalid(): void {
    if (this.quoteForm.author().invalid()) {
      this.quoteForm.author().focusBoundControl();
    } else if (this.quoteForm.text().invalid()) {
      this.quoteForm.text().focusBoundControl();
    }
  }

  private messageForError(err: HttpErrorResponse): string {
    if (err.status === 400) {
      return typeof err.error === 'string' && err.error.length > 0
        ? err.error
        : 'The quote could not be created. Check the fields and try again.';
    }

    if (err.status === 401 || err.status === 403) {
      return 'You do not have permission to create quotes.';
    }

    return 'Failed to create the quote. Is QuotesApi running on http://localhost:5228?';
  }
}
