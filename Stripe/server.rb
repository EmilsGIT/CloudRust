require 'stripe'
require 'sinatra'

stripe_secret_key = ENV.fetch('STRIPE_SECRET_KEY') do
  raise 'Missing STRIPE_SECRET_KEY environment variable'
end

client = Stripe::StripeClient.new(stripe_secret_key)

set :static, true
set :port, 4242

YOUR_DOMAIN = 'http://localhost:4242'

post '/create-checkout-session' do
  prices = client.v1.prices.list(
    lookup_keys: [params['lookup_key']],
    expand: ['data.product']
  )

  begin
    session = client.v1.checkout.sessions.create({
      mode: 'subscription',
      line_items: [{
        quantity: 1,
        price: prices.data[0].id
      }],
      success_url: YOUR_DOMAIN + '/success.html?session_id={CHECKOUT_SESSION_ID}',
    })
  rescue StandardError => e
    halt 400,
        { 'Content-Type' => 'application/json' },
        { 'error': { message: e.error.message } }.to_json
  end

  redirect session.url, 303
end

post '/create-portal-session' do
  content_type 'application/json'
  # For demonstration purposes, we're using the Checkout session to retrieve the customer_account ID.
  # Typically this is stored alongside the authenticated user in your database.
  checkout_session_id = params['session_id']
  checkout_session = client.v1.checkout.sessions.retrieve(checkout_session_id)

  # This is the URL to which users will be redirected after they're done
  # managing their billing.
  return_url = YOUR_DOMAIN

  session = client.v1.billing_portal.sessions.create({

                                                    customer: checkout_session.customer,
                                                    return_url: return_url
                                                  })
  redirect session.url, 303
end

post '/webhook' do
  # Replace this endpoint secret with your endpoint's unique secret
  # If you are testing with the CLI, find the secret by running 'stripe listen'
  # If you are using an endpoint defined with the API or dashboard, look in your webhook settings
  # at https://dashboard.stripe.com/webhooks
  webhook_secret = ENV.fetch('STRIPE_WEBHOOK_SECRET', '')
  payload = request.body.read
  if !webhook_secret.empty?
    # Retrieve the event by verifying the signature using the raw body and secret if webhook signing is configured.
    sig_header = request.env['HTTP_STRIPE_SIGNATURE']
    event = nil

    begin
      event = Stripe::Webhook.construct_event(
        payload, sig_header, webhook_secret
      )
    rescue JSON::ParserError => e
      # Invalid payload
      status 400
      return
    rescue Stripe::SignatureVerificationError => e
      # Invalid signature
      puts '⚠️  Webhook signature verification failed.'
      status 400
      return
    end
  else
    data = JSON.parse(payload, symbolize_names: true)
    event = Stripe::Event.construct_from(data)
  end
  # Get the type of webhook event sent - used to check the status of PaymentIntents.
  event_type = event['type']
  data = event['data']
  data_object = data['object']

  if event.type == 'customer.subscription.deleted'
    # handle subscription canceled automatically based
    # upon your subscription settings. Or if the user cancels it.
    # puts data_object
    puts "Subscription canceled: #{event.id}"
  end

  if event.type == 'customer.subscription.updated'
    # handle subscription updated
    # puts data_object
    puts "Subscription updated: #{event.id}"
  end

  if event.type == 'customer.subscription.created'
    # handle subscription created
    # puts data_object
    puts "Subscription created: #{event.id}"
  end

  if event.type == 'customer.subscription.trial_will_end'
    # handle subscription trial ending
    # puts data_object
    puts "Subscription trial will end: #{event.id}"
  end

  if event.type == 'entitlements.active_entitlement_summary.updated'
    # handle active entitlement summary updated
    # puts data_object
    puts "Active entitlement summary updated: #{event.id}"
  end

  content_type 'application/json'
  {
    status: 'success'
  }.to_json
end