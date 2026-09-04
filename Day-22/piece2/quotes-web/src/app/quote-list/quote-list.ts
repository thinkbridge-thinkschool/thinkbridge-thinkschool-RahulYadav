import { Component, inject, input, output } from '@angular/core';
import { QuoteListState } from './quote-list-state';

@Component({
  selector: 'app-quote-list',
  imports: [],
  templateUrl: './quote-list.html',
  styleUrl: './quote-list.css',
  providers: [QuoteListState],
})
export class QuoteList {
  readonly state = inject(QuoteListState);

  // Currently selected quote id, owned by the parent — used only to highlight a row.
  readonly selectedId = input<number | null>(null);

  // Emitted when the user picks a quote from the list.
  readonly select = output<number>();

  constructor() {
    this.state.loadQuotes(this.state.page(), this.state.pageSize());
  }

  previousPage(): void {
    this.state.previousPage();
  }

  nextPage(): void {
    this.state.nextPage();
  }

  changePageSize(size: number): void {
    this.state.changePageSize(size);
  }

  selectQuote(id: number): void {
    this.select.emit(id);
  }
}
