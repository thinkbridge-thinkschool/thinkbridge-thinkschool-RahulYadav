import { Component, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { QuoteService } from '../core/quote.service';
import type { Quote } from '../core/quote.model';

// Matches QuotesApi.Models.Quote's constants exactly (see Quote.Create):
// IsNullOrWhiteSpace -> required, then trimmed length must fall in range.
const AUTHOR_MIN = 1; // Quote.MinAuthorLength
const AUTHOR_MAX = 200; // Quote.MaxAuthorLength
const TEXT_MIN = 1; // Quote.MinTextLength
const TEXT_MAX = 1000; // Quote.MaxTextLength

function quoteFieldValidator(min: number, max: number): ValidatorFn {
  return (control): ValidationErrors | null => {
    const value = (control.value as string | null) ?? '';
    const trimmed = value.trim();

    if (trimmed.length === 0) {
      return { required: true };
    }

    if (trimmed.length < min || trimmed.length > max) {
      return { length: { min, max, actual: trimmed.length } };
    }

    return null;
  };
}

@Component({
  selector: 'app-quote-create',
  imports: [ReactiveFormsModule],
  templateUrl: './quote-create.html',
  styleUrl: './quote-create.css',
})
export class QuoteCreate {
  private readonly fb = inject(FormBuilder);
  private readonly quoteService = inject(QuoteService);

  private readonly authorInput = viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput = viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  // Emitted with the created quote so the parent can show it, matching the
  // existing selectedId-in-parent pattern used by QuoteList's `select` output.
  readonly created = output<Quote>();

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    author: this.fb.nonNullable.control('', quoteFieldValidator(AUTHOR_MIN, AUTHOR_MAX)),
    text: this.fb.nonNullable.control('', quoteFieldValidator(TEXT_MIN, TEXT_MAX)),
  });

  get author() {
    return this.form.controls.author;
  }

  get text() {
    return this.form.controls.text;
  }

  authorErrorMessage(): string | null {
    return this.describeError('Author', this.author.errors);
  }

  textErrorMessage(): string | null {
    return this.describeError('Text', this.text.errors);
  }

  submit(): void {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidControl();
      return;
    }

    this.submitting.set(true);
    this.submitError.set(null);

    this.quoteService
      .createQuote({
        author: this.author.value.trim(),
        text: this.text.value.trim(),
      })
      .subscribe({
        next: (quote) => {
          this.submitting.set(false);
          this.form.reset();
          this.created.emit(quote);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.submitError.set(this.messageForError(err));
        },
      });
  }

  private describeError(label: string, errors: ValidationErrors | null): string | null {
    if (!errors) {
      return null;
    }

    if (errors['required']) {
      return `${label} is required.`;
    }

    if (errors['length']) {
      const { min, max } = errors['length'] as { min: number; max: number };
      return `${label} must be ${min}-${max} characters.`;
    }

    return null;
  }

  private focusFirstInvalidControl(): void {
    if (this.author.invalid) {
      this.authorInput()?.nativeElement.focus();
    } else if (this.text.invalid) {
      this.textInput()?.nativeElement.focus();
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
