import { Component, signal } from '@angular/core';
import { QuoteList } from './quote-list/quote-list';
import { QuoteDetail } from './quote-detail/quote-detail';

@Component({
  selector: 'app-root',
  imports: [QuoteList, QuoteDetail],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected readonly title = signal('QuotesApi frontend');

  // Owned by the root component: passed down to QuoteList for highlighting
  // and to QuoteDetail to drive the detail fetch.
  protected readonly selectedId = signal<number | null>(null);
}
