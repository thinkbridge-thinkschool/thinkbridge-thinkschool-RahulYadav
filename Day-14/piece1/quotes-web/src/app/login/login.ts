import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);

  private readonly emailInput = viewChild<ElementRef<HTMLInputElement>>('emailInput');
  private readonly passwordInput = viewChild<ElementRef<HTMLInputElement>>('passwordInput');

  readonly submitting = signal(false);
  readonly loginError = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    password: this.fb.nonNullable.control('', Validators.required),
  });

  get email() {
    return this.form.controls.email;
  }

  get password() {
    return this.form.controls.password;
  }

  emailErrorMessage(): string | null {
    return this.describeError('Email', this.email.errors);
  }

  passwordErrorMessage(): string | null {
    return this.describeError('Password', this.password.errors);
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
    this.loginError.set(null);

    this.authService
      .login({
        email: this.email.value.trim(),
        password: this.password.value,
      })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.form.reset();
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.loginError.set(this.messageForError(err));
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

    if (errors['email']) {
      return 'Enter a valid email address.';
    }

    return null;
  }

  private focusFirstInvalidControl(): void {
    if (this.email.invalid) {
      this.emailInput()?.nativeElement.focus();
    } else if (this.password.invalid) {
      this.passwordInput()?.nativeElement.focus();
    }
  }

  private messageForError(err: HttpErrorResponse): string {
    if (err.status === 401) {
      return 'Incorrect email or password.';
    }

    return 'Failed to log in. Is QuotesApi running on http://localhost:5228?';
  }
}
