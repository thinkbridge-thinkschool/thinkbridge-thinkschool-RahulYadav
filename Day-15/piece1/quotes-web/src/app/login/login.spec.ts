import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { Login } from './login';

describe('Login', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  function query<T extends Element>(fixture: { nativeElement: HTMLElement }, selector: string): T {
    const el = fixture.nativeElement.querySelector(selector);
    if (!el) {
      throw new Error(`expected to find ${selector}`);
    }
    return el as T;
  }

  it('renders labelled, accessible fields with no errors on an empty form', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await fixture.whenStable();

    const email = query<HTMLInputElement>(fixture, '#login-email');
    const password = query<HTMLInputElement>(fixture, '#login-password');
    const emailLabel = query<HTMLLabelElement>(fixture, 'label[for="login-email"]');
    const passwordLabel = query<HTMLLabelElement>(fixture, 'label[for="login-password"]');

    expect(emailLabel.textContent).toContain('Email');
    expect(passwordLabel.textContent).toContain('Password');
    expect(password.type).toBe('password');
    expect(email.hasAttribute('aria-invalid')).toBe(false);
    expect(password.hasAttribute('aria-invalid')).toBe(false);
    expect(fixture.nativeElement.querySelector('.field-error')).toBeNull();
  });

  it('marks both fields invalid and focuses email first when submitting empty', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await fixture.whenStable();

    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    await fixture.whenStable();

    const email = query<HTMLInputElement>(fixture, '#login-email');
    const emailError = query<HTMLElement>(fixture, '#login-email-error');
    const passwordError = query<HTMLElement>(fixture, '#login-password-error');

    expect(email.getAttribute('aria-invalid')).toBe('true');
    expect(email.getAttribute('aria-describedby')).toBe('login-email-error');
    expect(emailError.textContent).toContain('Email is required.');
    expect(passwordError.textContent).toContain('Password is required.');
    expect(fixture.nativeElement.ownerDocument.activeElement).toBe(email);
  });

  it('shows an invalid-email message for a malformed address', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await fixture.whenStable();

    const email = query<HTMLInputElement>(fixture, '#login-email');
    email.value = 'not-an-email';
    email.dispatchEvent(new Event('input'));
    email.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(email.getAttribute('aria-invalid')).toBe('true');
    const error = query<HTMLElement>(fixture, '#login-email-error');
    expect(error.textContent).toContain('Enter a valid email address.');
  });

  it('logs in successfully, shows a submitting state, and prevents duplicate submissions', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await fixture.whenStable();

    const email = query<HTMLInputElement>(fixture, '#login-email');
    const password = query<HTMLInputElement>(fixture, '#login-password');
    email.value = 'ada@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'secret';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const button = query<HTMLButtonElement>(fixture, 'button[type="submit"]');
    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('Logging in');

    // Submitting again while in flight must not issue a second request.
    form.dispatchEvent(new Event('submit'));

    const req = httpMock.expectOne((r) => r.url === '/api/auth/login' && r.method === 'POST');
    expect(req.request.body).toEqual({ email: 'ada@example.com', password: 'secret' });

    req.flush({ accessToken: 'access-123', refreshToken: 'refresh-456', expiresIn: 900 });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(button.disabled).toBe(false);
  });

  it('shows an accessible error for incorrect credentials and re-enables the form', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await fixture.whenStable();

    const email = query<HTMLInputElement>(fixture, '#login-email');
    const password = query<HTMLInputElement>(fixture, '#login-password');
    email.value = 'ada@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'wrong-password';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const req = httpMock.expectOne((r) => r.url === '/api/auth/login' && r.method === 'POST');
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    const error = query<HTMLElement>(fixture, '#login-error');
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toContain('Incorrect email or password.');

    const button = query<HTMLButtonElement>(fixture, 'button[type="submit"]');
    expect(button.disabled).toBe(false);

    // The password value itself must never surface in the DOM/error text.
    expect(fixture.nativeElement.textContent).not.toContain('wrong-password');
  });

  it('shows a generic accessible error for a server failure', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await fixture.whenStable();

    const email = query<HTMLInputElement>(fixture, '#login-email');
    const password = query<HTMLInputElement>(fixture, '#login-password');
    email.value = 'ada@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'secret';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const form = query<HTMLFormElement>(fixture, 'form');
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const req = httpMock.expectOne((r) => r.url === '/api/auth/login' && r.method === 'POST');
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    const error = query<HTMLElement>(fixture, '#login-error');
    expect(error.getAttribute('role')).toBe('alert');
    expect(error.textContent).toContain('Failed to log in');
  });
});
